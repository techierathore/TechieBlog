using Dapper;
using BlogModels;

namespace BlogEngine.DbAccess;

/// <summary>
/// Repository for managing UserEvent data access operations using Dapper ORM.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Handles CRUD operations for user events including
/// work experience, speaking engagements, and other timeline events.</para>
/// </remarks>
public class UserEventRepo : GenericRepository<UserEvent>, IUserEventRepo
{
    public UserEventRepo(string connectionString) : base(connectionString) { }

    public override IEnumerable<UserEvent> GetAll()
    {
        using var vConn = GetOpenConnection();
        return vConn.Query<UserEvent>(
            @"SELECT eventid, logoiconpath, eventtitle, sessiontitle, eventurl,
                     eventdate, type AS eventtype, userid, startdate, description,
                     displayorder, iscurrent
              FROM userevents
              ORDER BY displayorder, eventdate DESC");
    }

    public override IEnumerable<UserEvent> GetAllById(long aSingleId)
    {
        using var vConn = GetOpenConnection();
        return vConn.Query<UserEvent>(
            @"SELECT eventid, logoiconpath, eventtitle, sessiontitle, eventurl,
                     eventdate, type AS eventtype, userid, startdate, description,
                     displayorder, iscurrent
              FROM userevents
              WHERE userid = @UserId
              ORDER BY displayorder, eventdate DESC",
            new { UserId = aSingleId }).ToList();
    }

    /// <summary>
    /// Gets all events for a user filtered by event type.
    /// </summary>
    /// <param name="userId">The user's ID.</param>
    /// <param name="eventType">The event type to filter by (e.g., "Experience", "Speaking").</param>
    /// <returns>Collection of matching events ordered by DisplayOrder.</returns>
    public IEnumerable<UserEvent> GetByUserAndType(long userId, string eventType)
    {
        using var vConn = GetOpenConnection();
        return vConn.Query<UserEvent>(
            @"SELECT eventid, logoiconpath, eventtitle, sessiontitle, eventurl,
                     eventdate, type AS eventtype, userid, startdate, description,
                     displayorder, iscurrent
              FROM userevents
              WHERE userid = @UserId AND type = @EventType
              ORDER BY displayorder, eventdate DESC",
            new { UserId = userId, EventType = eventType }).ToList();
    }

    public override UserEvent GetIntSingle(int aSingleId)
    {
        return GetSingle((long)aSingleId);
    }

    public override IEnumerable<UserEvent> GetPagedData(int PageSize, int OffSet)
    {
        using var vConn = GetOpenConnection();
        return vConn.Query<UserEvent>(
            @"SELECT eventid, logoiconpath, eventtitle, sessiontitle, eventurl,
                     eventdate, type AS eventtype, userid, startdate, description,
                     displayorder, iscurrent
              FROM userevents
              ORDER BY displayorder, eventdate DESC
              LIMIT @PageSize OFFSET @OffSet",
            new { PageSize, OffSet });
    }

    public override UserEvent GetSingle(long aEventID)
    {
        using var vConn = GetOpenConnection();
        return vConn.QueryFirstOrDefault<UserEvent>(
            @"SELECT eventid, logoiconpath, eventtitle, sessiontitle, eventurl,
                     eventdate, type AS eventtype, userid, startdate, description,
                     displayorder, iscurrent
              FROM userevents
              WHERE eventid = @EventId",
            new { EventId = aEventID });
    }

    public override void Insert(UserEvent aEntity)
    {
        using var vConn = GetOpenConnection();
        vConn.Execute(
            @"INSERT INTO userevents (logoiconpath, eventtitle, sessiontitle, eventurl,
                                      eventdate, type, userid, startdate, description,
                                      displayorder, iscurrent)
              VALUES (@LogoIconPath, @EventTitle, @SessionTitle, @EventUrl, @EventDate,
                      @EventType, @UserID, @StartDate, @Description, @DisplayOrder, @IsCurrent)",
            new
            {
                aEntity.LogoIconPath,
                aEntity.EventTitle,
                aEntity.SessionTitle,
                aEntity.EventUrl,
                aEntity.EventDate,
                aEntity.EventType,
                aEntity.UserID,
                aEntity.StartDate,
                aEntity.Description,
                aEntity.DisplayOrder,
                aEntity.IsCurrent
            });
    }

    public override long InsertToGetId(UserEvent entity)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            INSERT INTO userevents (logoiconpath, eventtitle, sessiontitle, eventurl,
                                    eventdate, type, userid, startdate, description,
                                    displayorder, iscurrent)
            VALUES (@LogoIconPath, @EventTitle, @SessionTitle, @EventUrl, @EventDate,
                    @EventType, @UserID, @StartDate, @Description, @DisplayOrder, @IsCurrent)
            RETURNING eventid";
        return vConn.ExecuteScalar<long>(sql, new
        {
            entity.LogoIconPath,
            entity.EventTitle,
            entity.SessionTitle,
            entity.EventUrl,
            entity.EventDate,
            entity.EventType,
            entity.UserID,
            entity.StartDate,
            entity.Description,
            entity.DisplayOrder,
            entity.IsCurrent
        });
    }

    public override void Update(UserEvent aEntity)
    {
        using var vConn = GetOpenConnection();
        vConn.Execute(
            @"UPDATE userevents
              SET logoiconpath = @LogoIconPath,
                  eventtitle = @EventTitle,
                  sessiontitle = @SessionTitle,
                  eventurl = @EventUrl,
                  eventdate = @EventDate,
                  type = @EventType,
                  userid = @UserID,
                  startdate = @StartDate,
                  description = @Description,
                  displayorder = @DisplayOrder,
                  iscurrent = @IsCurrent
              WHERE eventid = @EventID",
            new
            {
                aEntity.EventID,
                aEntity.LogoIconPath,
                aEntity.EventTitle,
                aEntity.SessionTitle,
                aEntity.EventUrl,
                aEntity.EventDate,
                aEntity.EventType,
                aEntity.UserID,
                aEntity.StartDate,
                aEntity.Description,
                aEntity.DisplayOrder,
                aEntity.IsCurrent
            });
    }

    /// <summary>
    /// Deletes an event by ID.
    /// </summary>
    /// <param name="eventId">Event ID to delete.</param>
    public void Delete(long eventId)
    {
        using var vConn = GetOpenConnection();
        vConn.Execute("DELETE FROM userevents WHERE eventid = @EventId", new { EventId = eventId });
    }

    /// <summary>
    /// Updates the display order for multiple events.
    /// </summary>
    /// <param name="eventOrders">Dictionary of EventId to DisplayOrder.</param>
    public void UpdateDisplayOrders(Dictionary<long, int> eventOrders)
    {
        using var vConn = GetOpenConnection();
        foreach (var kvp in eventOrders)
        {
            vConn.Execute(
                "UPDATE userevents SET displayorder = @DisplayOrder WHERE eventid = @EventId",
                new { EventId = kvp.Key, DisplayOrder = kvp.Value });
        }
    }
}
