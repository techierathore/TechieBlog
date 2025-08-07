namespace BlogEngine.DbAccess;

public class LoginLogRepo : GenericRepository<LoginLog>, ILoginLogRepo
{
    public LoginLogRepo(string connectionString) : base(connectionString) { }

    public override IEnumerable<LoginLog> GetAll()
    {
        using var vConn = GetOpenConnection();
        return vConn.Query<LoginLog>("GetLoginLogs", commandType: CommandType.StoredProcedure);
    }

    public override LoginLog GetIntSingle(int aOrgId)
    {
        throw new NotImplementedException();
    }

    public override LoginLog GetSingle(long aSingleId)
    {
        using var vConn = GetOpenConnection();
        var vParams = new DynamicParameters();
        vParams.Add("@pLoginLogId", aSingleId);
        return vConn.QueryFirstOrDefault<LoginLog>("LoginLogSelect", vParams, commandType: CommandType.StoredProcedure);
    }
    public IEnumerable<LoginLog> GetUserLoginLogs(long aAppUserId)
    {
        using var vConn = GetOpenConnection();
        var vParams = new DynamicParameters();
        vParams.Add("@pLoginUserId", aAppUserId);
        return vConn.Query<LoginLog>("GetUserLoginLogs", vParams, commandType: CommandType.StoredProcedure);
    }
    public override void Insert(LoginLog aLoginLog)
    {
        using var vConn = GetOpenConnection();
        var vParams = new DynamicParameters();
        vParams.Add("@pLoginLogId", aLoginLog.LoginLogId);
        vParams.Add("@pLoginUserId", aLoginLog.LoginUserId);
        vParams.Add("@pLoginDateTime", aLoginLog.LoginDateTime);
        vParams.Add("@pClientIP", aLoginLog.ClientIP);
        vConn.Execute("LoginLogInsert", vParams, commandType: CommandType.StoredProcedure);
    }

    public bool UpdateLogOut(long aAppUserId, DateTime aDtLogOut)
    {
        var blResult = false;
        using (var vConn = GetOpenConnection())
        {
            var vParams = new DynamicParameters();
            vParams.Add("@pLoginUserId", aAppUserId);
            vParams.Add("@pLogOutDateTime", aDtLogOut);
            int iResult = vConn.Execute("UpdateLogOut", vParams, commandType: CommandType.StoredProcedure);
            if (iResult == 0) blResult = true;
        }
        return blResult;
    }
    public override void Update(LoginLog aLoginLog)
    {
        throw new NotImplementedException();
    }

    public override IEnumerable<LoginLog> GetAllById(long aSingleId)
    {
        throw new NotImplementedException();
    }

    public override long InsertToGetId(LoginLog entity)
    {
        throw new NotImplementedException();
    }

    public override IEnumerable<LoginLog> GetPagedData(int PageSize, int OffSet)
    {
        throw new NotImplementedException();
    }
}
