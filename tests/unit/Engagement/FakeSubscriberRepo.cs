using System.Data;
using BlogModels;

namespace TechieBlog.Tests.Engagement;

/// <summary>
/// In-memory stand-in for <see cref="ISubscriberRepo"/>.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Lets the verification tests prove that redeeming a
/// <see cref="EmailVerificationPurpose.Subscription"/> token actually confirms the pending
/// subscriber, without a database. [REQ-UI-055]</para>
/// <para><b>Code Flow:</b> <see cref="UpdateStatus"/> reproduces the single-column UPDATE the
/// real repository issues against <c>Subscriber.IsConfirmed</c>.</para>
/// <para><b>Dependencies:</b> None.</para>
/// <para><b>Usage:</b> Seed with <see cref="InsertToGetId"/> and assert on
/// <see cref="Subscribers"/> after the act step.</para>
/// </remarks>
public class FakeSubscriberRepo : ISubscriberRepo
{
    private readonly List<Subscriber> subscribers = new();
    private long nextId = 1;

    /// <summary>
    /// Gets the subscriber rows this fake currently holds.
    /// </summary>
    public IReadOnlyList<Subscriber> Subscribers => subscribers;

    /// <inheritdoc />
    public Subscriber? GetByEmail(string email)
    {
        return subscribers.FirstOrDefault(s =>
            string.Equals(s.Email, email, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public bool EmailExists(string email) => GetByEmail(email) != null;

    /// <inheritdoc />
    public IEnumerable<Subscriber> GetActiveSubscribers() =>
        subscribers.Where(s => s.IsConfirmed).ToList();

    /// <inheritdoc />
    public IEnumerable<Subscriber> GetByStatus(bool isActive) =>
        subscribers.Where(s => s.IsConfirmed == isActive).ToList();

    /// <inheritdoc />
    public IEnumerable<Subscriber> SearchByEmail(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return subscribers.ToList();

        return subscribers
            .Where(s => s.Email != null &&
                        s.Email.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <inheritdoc />
    public void UpdateStatus(long subscriberId, bool isActive)
    {
        var existing = subscribers.FirstOrDefault(s => s.SubscriberId == subscriberId);
        if (existing == null)
            return;

        existing.IsConfirmed = isActive;
        existing.IsActive = isActive;
    }

    /// <inheritdoc />
    public int GetTotalCount() => subscribers.Count;

    /// <inheritdoc />
    public int GetActiveCount() => subscribers.Count(s => s.IsConfirmed);

    /// <inheritdoc />
    public IDbConnection GetOpenConnection() =>
        throw new NotSupportedException("The fake repository has no database.");

    /// <inheritdoc />
    public long InsertToGetId(Subscriber subscriber)
    {
        subscriber.SubscriberId = nextId++;
        subscribers.Add(subscriber);
        return subscriber.SubscriberId;
    }

    /// <inheritdoc />
    public void Insert(Subscriber subscriber) => InsertToGetId(subscriber);

    /// <inheritdoc />
    public void Update(Subscriber subscriberToUpdate)
    {
        var existing = subscribers.FirstOrDefault(s => s.SubscriberId == subscriberToUpdate.SubscriberId);
        if (existing == null)
            return;

        existing.Email = subscriberToUpdate.Email;
        existing.Name = subscriberToUpdate.Name;
        existing.IsConfirmed = subscriberToUpdate.IsConfirmed;
        existing.Preferences = subscriberToUpdate.Preferences;
    }

    /// <inheritdoc />
    public Subscriber? GetSingle(long subscriberId) =>
        subscribers.FirstOrDefault(s => s.SubscriberId == subscriberId);

    /// <inheritdoc />
    public Subscriber? GetIntSingle(int subscriberId) => GetSingle(subscriberId);

    /// <inheritdoc />
    public IEnumerable<Subscriber> GetAll() => subscribers.ToList();

    /// <inheritdoc />
    public IEnumerable<Subscriber> GetPagedData(int pageSize, int offSet) =>
        subscribers.Skip(offSet).Take(pageSize).ToList();

    /// <inheritdoc />
    public IEnumerable<Subscriber> GetAllById(long subscriberId) =>
        subscribers.Where(s => s.SubscriberId == subscriberId).ToList();
}
