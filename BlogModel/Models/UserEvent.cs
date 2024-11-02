namespace BlogModels;

public class UserEvent
{
    /// <summary>
    /// Gets or sets the EventsID value.
    /// </summary>
    public long EventID
    { get; set; }

    /// <summary>
    /// Gets or sets the LogoIconPath value.
    /// </summary>
    public string LogoIconPath
    { get; set; }
    /// <summary>
    /// Gets or sets the SessionTitle value.
    /// </summary>
    public string SessionTitle
    { get; set; }

    /// <summary>
    /// Gets or sets the EventTitle value.
    /// </summary>
    public string EventTitle
    { get; set; }

    /// <summary>
    /// Gets or sets the EventUrl value.
    /// </summary>
    public string EventUrl
    { get; set; }

    /// <summary>
    /// Gets or sets the Type  value.
    /// </summary>
    public string EventType
    { get; set; }

    /// <summary>
    /// Gets or sets the EventDate  value.
    /// </summary>
    public DateTime EventDate
    { get; set; }

    /// <summary>
    /// Gets or sets the UserID value.
    /// </summary>
    public long UserID
    { get; set; }

    public string UIPageTitle
    { get; set; }
}
