namespace BlogEngine.DbAccess;

/// <summary>
/// Repository for managing UserEvent data access operations using Dapper ORM.
/// </summary>
public class UserEventRepo : GenericRepository<UserEvent>, IUserEventRepo
{
    public UserEventRepo(string connectionString) : base(connectionString) { }

    public override IEnumerable<UserEvent> GetAll()
    {
        using var vConn = GetOpenConnection();
        return vConn.Query<UserEvent>("SELECT * FROM userevents ORDER BY eventdate DESC");
    }

    public override IEnumerable<UserEvent> GetAllById(long aSingleId)
    {
        using var vConn = GetOpenConnection();
        return vConn.Query<UserEvent>(
            "SELECT * FROM userevents WHERE userid = @UserId ORDER BY eventdate DESC",
            new { UserId = aSingleId }).ToList();
    }

    public override UserEvent GetIntSingle(int aSingleId)
    {
        throw new NotImplementedException();
    }

    public override IEnumerable<UserEvent> GetPagedData(int PageSize, int OffSet)
    {
        using var vConn = GetOpenConnection();
        return vConn.Query<UserEvent>(
            @"SELECT * FROM userevents ORDER BY eventdate DESC LIMIT @PageSize OFFSET @OffSet",
            new { PageSize, OffSet });
    }

    public override UserEvent GetSingle(long aEventID)
    {
        using var vConn = GetOpenConnection();
        return vConn.QueryFirstOrDefault<UserEvent>(
            "SELECT * FROM userevents WHERE eventid = @EventId",
            new { EventId = aEventID });
    }

    public override void Insert(UserEvent aEntity)
    {
        using var vConn = GetOpenConnection();
        vConn.Execute(
            @"INSERT INTO userevents (logoiconpath, eventtitle, sessiontitle, eventurl, eventdate, type, userid)
              VALUES (@LogoIconPath, @EventTitle, @SessionTitle, @EventUrl, @EventDate, @EventType, @UserID)",
            new
            {
                aEntity.LogoIconPath,
                aEntity.EventTitle,
                aEntity.SessionTitle,
                aEntity.EventUrl,
                aEntity.EventDate,
                aEntity.EventType,
                aEntity.UserID
            });
    }

    public override long InsertToGetId(UserEvent entity)
    {
        throw new NotImplementedException();
    }

    public override void Update(UserEvent aEntity)
    {
        using var vConn = GetOpenConnection();
        vConn.Execute(
            @"UPDATE userevents
              SET logoiconpath = @LogoIconPath, eventtitle = @EventTitle, sessiontitle = @SessionTitle,
                  eventurl = @EventUrl, eventdate = @EventDate, type = @EventType, userid = @UserID
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
                aEntity.UserID
            });
    }
}
