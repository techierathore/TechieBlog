/// <summary>
/// Represents an email newsletter subscriber.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Stores subscriber information for newsletter features.</para>
/// <para><b>Usage:</b> Used by SubscriberRepo for data access and SubscriberSvc for business logic.</para>
/// </remarks>
namespace BlogModels;

public class Subscriber
{
    /// <summary>
    /// Unique identifier for the subscriber.
    /// </summary>
    public long SubscriberId { get; set; }

    /// <summary>
    /// Subscriber's email address (unique).
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Subscriber's display name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Date when the subscriber signed up.
    /// </summary>
    public DateTime SubscribedOn { get; set; }

    /// <summary>
    /// Whether the subscription is confirmed (double opt-in).
    /// </summary>
    public bool IsConfirmed { get; set; }

    /// <summary>
    /// Whether the subscriber is active (not unsubscribed).
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// JSON preferences for email topics (optional).
    /// </summary>
    public string Preferences { get; set; } = string.Empty;
}
