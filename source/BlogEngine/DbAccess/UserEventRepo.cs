namespace BlogEngine.DbAccess;

public class UserEventRepo : GenericRepository<UserEvent>, IUserEventRepo
{
    public UserEventRepo(string connectionString) : base(connectionString) { }
    public override IEnumerable<UserEvent> GetAll()
    {
        throw new NotImplementedException();
    }

    public override IEnumerable<UserEvent> GetAllById(long aSingleId)
    {
        using var vConn = GetOpenConnection();
        var vParams = new DynamicParameters();
        vParams.Add("@BlogUserID", aSingleId);
        return vConn.Query<UserEvent>("GetUserEvents", vParams, commandType: CommandType.StoredProcedure).ToList();
    }

    public override UserEvent GetIntSingle(int aSingleId)
    {
        throw new NotImplementedException();
    }

    public override IEnumerable<UserEvent> GetPagedData(int PageSize, int OffSet)
    {
        throw new NotImplementedException();
    }

    public override UserEvent GetSingle(long aEventID)
    {
        using var vConn = GetOpenConnection();
        var vParams = new DynamicParameters();
        vParams.Add("@UserEventID", aEventID);
        return vConn.Query<UserEvent>("UserEventSelect", vParams, commandType: CommandType.StoredProcedure).FirstOrDefault();
    }

    public override void Insert(UserEvent aEntity)
    {
        using var vConn = GetOpenConnection();
        var vParams = new DynamicParameters();
        vParams.Add("@pLogoIconPath", aEntity.LogoIconPath);
        vParams.Add("@pEventTitle", aEntity.EventTitle);
        vParams.Add("@pSessionTitle", aEntity.SessionTitle);
        vParams.Add("@pEventUrl", aEntity.EventUrl);
        vParams.Add("@pEventDate", aEntity.EventDate);
        vParams.Add("@pType", aEntity.EventType);
        vParams.Add("@BlogUserID", aEntity.UserID);
        int iResult = vConn.Execute("UserEventInsert", vParams, commandType: CommandType.StoredProcedure);
    }

    public override long InsertToGetId(UserEvent entity)
    {
        throw new NotImplementedException();
    }

    public override void Update(UserEvent aEntity)
    {
        using var vConn = GetOpenConnection();
        var vParams = new DynamicParameters();
        vParams.Add("@UserEventID", aEntity.EventID);
        vParams.Add("@pLogoIconPath", aEntity.LogoIconPath);
        vParams.Add("@pEventTitle", aEntity.EventTitle);
        vParams.Add("@pSessionTitle", aEntity.SessionTitle);
        vParams.Add("@pEventUrl", aEntity.EventUrl);
        vParams.Add("@pEventDate", aEntity.EventDate);
        vParams.Add("@pType", aEntity.EventType);
        vParams.Add("@pUserID", aEntity.UserID);
        int iResult = vConn.Execute("UserEventUpdate", vParams, commandType: CommandType.StoredProcedure);
    }
}
