using BlogModels;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace BlogEngine.Services;

/// <summary>
/// Service layer for subscriber operations.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides business logic for newsletter subscriber management.</para>
/// <para><b>Dependencies:</b> ISubscriberRepo for data access.</para>
/// </remarks>
public class SubscriberSvc
{
    private readonly ISubscriberRepo _subscriberRepo;
    private readonly ILogger<SubscriberSvc> _logger;

    // Simple email validation regex
    private static readonly Regex EmailRegex = new Regex(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public SubscriberSvc(ISubscriberRepo subscriberRepo, ILogger<SubscriberSvc> logger)
    {
        _subscriberRepo = subscriberRepo;
        _logger = logger;
    }

    /// <summary>
    /// Subscribes a new email address.
    /// </summary>
    /// <param name="email">Email address to subscribe.</param>
    /// <param name="name">Optional subscriber name.</param>
    /// <returns>Result with subscriber on success, error message on failure.</returns>
    public Result<Subscriber> Subscribe(string email, string name = "")
    {
        // Validate email
        if (string.IsNullOrWhiteSpace(email))
            return Result<Subscriber>.Failure("Email address is required.");

        email = email.Trim().ToLower();

        if (!IsValidEmail(email))
            return Result<Subscriber>.Failure("Please enter a valid email address.");

        // Check for existing subscription
        if (_subscriberRepo.EmailExists(email))
        {
            var existing = _subscriberRepo.GetByEmail(email);
            if (existing != null && existing.IsActive)
                return Result<Subscriber>.Failure("This email is already subscribed.");

            // Reactivate inactive subscription
            if (existing != null && !existing.IsActive)
            {
                _subscriberRepo.UpdateStatus(existing.SubscriberId, true);
                existing.IsActive = true;
                _logger.LogInformation("Reactivated subscription for {Email}", email);
                return Result<Subscriber>.Success(existing);
            }
        }

        try
        {
            var subscriber = new Subscriber
            {
                Email = email,
                Name = string.IsNullOrWhiteSpace(name) ? email.Split('@')[0] : name.Trim(),
                SubscribedOn = DateTime.UtcNow,
                IsConfirmed = true, // Auto-confirm for now (no double opt-in)
                IsActive = true
            };

            subscriber.SubscriberId = _subscriberRepo.InsertToGetId(subscriber);
            _logger.LogInformation("New subscription created for {Email}", email);
            return Result<Subscriber>.Success(subscriber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create subscription for {Email}", email);
            return Result<Subscriber>.Failure("Failed to subscribe. Please try again later.");
        }
    }

    /// <summary>
    /// Unsubscribes an email address.
    /// </summary>
    /// <param name="email">Email to unsubscribe.</param>
    /// <returns>Result indicating success or failure.</returns>
    public Result Unsubscribe(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return Result.Failure("Email address is required.");

        var subscriber = _subscriberRepo.GetByEmail(email.Trim());
        if (subscriber == null)
            return Result.Failure("Email not found in subscribers list.");

        try
        {
            _subscriberRepo.UpdateStatus(subscriber.SubscriberId, false);
            _logger.LogInformation("Unsubscribed {Email}", email);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unsubscribe {Email}", email);
            return Result.Failure("Failed to unsubscribe. Please try again later.");
        }
    }

    /// <summary>
    /// Gets all subscribers.
    /// </summary>
    /// <returns>List of all subscribers.</returns>
    public IEnumerable<Subscriber> GetAllSubscribers()
    {
        try
        {
            return _subscriberRepo.GetAll();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all subscribers");
            return Enumerable.Empty<Subscriber>();
        }
    }

    /// <summary>
    /// Gets subscribers filtered by active status.
    /// </summary>
    /// <param name="isActive">Filter by active status.</param>
    /// <returns>Filtered list of subscribers.</returns>
    public IEnumerable<Subscriber> GetSubscribersByStatus(bool isActive)
    {
        try
        {
            return _subscriberRepo.GetByStatus(isActive);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting subscribers by status");
            return Enumerable.Empty<Subscriber>();
        }
    }

    /// <summary>
    /// Searches subscribers by email.
    /// </summary>
    /// <param name="query">Search query.</param>
    /// <returns>Matching subscribers.</returns>
    public IEnumerable<Subscriber> SearchSubscribers(string query)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(query))
                return _subscriberRepo.GetAll();
            return _subscriberRepo.SearchByEmail(query);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching subscribers with query: {Query}", query);
            return Enumerable.Empty<Subscriber>();
        }
    }

    /// <summary>
    /// Updates a subscriber's active status.
    /// </summary>
    /// <param name="subscriberId">Subscriber ID.</param>
    /// <param name="isActive">New active status.</param>
    /// <returns>Result indicating success or failure.</returns>
    public Result UpdateSubscriberStatus(long subscriberId, bool isActive)
    {
        try
        {
            var subscriber = _subscriberRepo.GetSingle(subscriberId);
            if (subscriber == null)
                return Result.Failure("Subscriber not found.");

            _subscriberRepo.UpdateStatus(subscriberId, isActive);
            _logger.LogInformation("Updated subscriber {Id} status to {Status}", subscriberId, isActive);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating subscriber status for ID {Id}", subscriberId);
            return Result.Failure("Failed to update subscriber status.");
        }
    }

    /// <summary>
    /// Gets subscriber statistics.
    /// </summary>
    /// <returns>Tuple of (total, active) counts.</returns>
    public (int Total, int Active) GetSubscriberStats()
    {
        try
        {
            return (_subscriberRepo.GetTotalCount(), _subscriberRepo.GetActiveCount());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting subscriber stats");
            return (0, 0);
        }
    }

    /// <summary>
    /// Gets all active subscribers for export.
    /// </summary>
    /// <returns>Active subscribers for CSV export.</returns>
    public IEnumerable<Subscriber> GetSubscribersForExport()
    {
        try
        {
            return _subscriberRepo.GetActiveSubscribers();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting subscribers for export");
            return Enumerable.Empty<Subscriber>();
        }
    }

    /// <summary>
    /// Validates an email address format.
    /// </summary>
    private bool IsValidEmail(string email)
    {
        return !string.IsNullOrWhiteSpace(email) && EmailRegex.IsMatch(email);
    }
}
