namespace BlogEngine.DbAccess;

/// <summary>
/// Repository for managing LoginLog data access operations using Dapper ORM.
/// </summary>
public class LoginLogRepo : GenericRepository<LoginLog>, ILoginLogRepo
{
    public LoginLogRepo(string connectionString) : base(connectionString) { }

    public override IEnumerable<LoginLog> GetAll()
    {
        using var vConn = GetOpenConnection();
        return vConn.Query<LoginLog>(
            @"SELECT logid AS LoginLogId, userid AS LoginUserId, attemptedon AS LoginDateTime,
                     ipaddress AS ClientIP
              FROM loginlog ORDER BY attemptedon DESC");
    }

    public override LoginLog GetIntSingle(int aOrgId)
    {
        return GetSingle((long)aOrgId);
    }

    public override LoginLog GetSingle(long aSingleId)
    {
        using var vConn = GetOpenConnection();
        return vConn.QueryFirstOrDefault<LoginLog>(
            @"SELECT logid AS LoginLogId, userid AS LoginUserId, attemptedon AS LoginDateTime,
                     ipaddress AS ClientIP
              FROM loginlog WHERE logid = @LogId",
            new { LogId = aSingleId });
    }

    public IEnumerable<LoginLog> GetUserLoginLogs(long aAppUserId)
    {
        using var vConn = GetOpenConnection();
        return vConn.Query<LoginLog>(
            @"SELECT logid AS LoginLogId, userid AS LoginUserId, attemptedon AS LoginDateTime,
                     ipaddress AS ClientIP
              FROM loginlog WHERE userid = @UserId ORDER BY attemptedon DESC",
            new { UserId = aAppUserId });
    }

    public override void Insert(LoginLog aLoginLog)
    {
        using var vConn = GetOpenConnection();
        vConn.Execute(
            @"INSERT INTO loginlog (userid, attemptedemail, success, ipaddress, attemptedon)
              VALUES (@LoginUserId, '', true, @ClientIP, @LoginDateTime)",
            new
            {
                aLoginLog.LoginUserId,
                aLoginLog.ClientIP,
                aLoginLog.LoginDateTime
            });
    }

    public bool UpdateLogOut(long aAppUserId, DateTime aDtLogOut)
    {
        // Note: LoginLog table doesn't have logout tracking in current schema
        // This is a stub for compatibility
        return true;
    }

    public override void Update(LoginLog aLoginLog)
    {
        using var vConn = GetOpenConnection();
        vConn.Execute(
            @"UPDATE loginlog SET
                userid = @LoginUserId,
                ipaddress = @ClientIP,
                attemptedon = @LoginDateTime
              WHERE logid = @LoginLogId",
            new
            {
                aLoginLog.LoginLogId,
                aLoginLog.LoginUserId,
                aLoginLog.ClientIP,
                aLoginLog.LoginDateTime
            });
    }

    public override IEnumerable<LoginLog> GetAllById(long aSingleId)
    {
        return GetUserLoginLogs(aSingleId);
    }

    public override long InsertToGetId(LoginLog entity)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            INSERT INTO loginlog (userid, attemptedemail, success, ipaddress, attemptedon)
            VALUES (@LoginUserId, '', true, @ClientIP, @LoginDateTime)
            RETURNING logid";
        return vConn.ExecuteScalar<long>(sql, new
        {
            entity.LoginUserId,
            entity.ClientIP,
            entity.LoginDateTime
        });
    }

    public override IEnumerable<LoginLog> GetPagedData(int PageSize, int OffSet)
    {
        using var vConn = GetOpenConnection();
        return vConn.Query<LoginLog>(
            @"SELECT logid AS LoginLogId, userid AS LoginUserId, attemptedon AS LoginDateTime,
                     ipaddress AS ClientIP
              FROM loginlog ORDER BY attemptedon DESC LIMIT @PageSize OFFSET @OffSet",
            new { PageSize, OffSet });
    }
}
