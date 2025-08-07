namespace BlogEngine.DbAccess;

public class SvcTokenRepo : GenericRepository<SvcToken>, ISvcTokenRepo
{
    public SvcTokenRepo(string connectionString) : base(connectionString) { }

    public override IEnumerable<SvcToken> GetAll()
    {
        using var vConn = GetOpenConnection();
        return vConn.Query<SvcToken>("LoginSvcTokenSelectAll", commandType: CommandType.StoredProcedure);
    }

    public override IEnumerable<SvcToken> GetAllById(long aSingleId)
    {
        throw new NotImplementedException();
    }

    public override SvcToken GetIntSingle(int aOrgId)
    {
        throw new NotImplementedException();
    }

    public override IEnumerable<SvcToken> GetPagedData(int PageSize, int OffSet)
    {
        throw new NotImplementedException();
    }

    public override SvcToken GetSingle(long aSingleId)
    {
        using var vConn = GetOpenConnection();
        var vParams = new DynamicParameters();
        vParams.Add("@aAppUserId", aSingleId);
        return vConn.QueryFirstOrDefault<SvcToken>("GetSvcToken", vParams, commandType: CommandType.StoredProcedure);
    }

    public SvcToken GetSvcToken(long aAppUserId, string aLoginToken)
    {
        using var vConn = GetOpenConnection();
        var vParams = new DynamicParameters();
        vParams.Add("@pAppUserId", aAppUserId);
        vParams.Add("@pLoginToken", aLoginToken);
        return vConn.Query<SvcToken>("TokenSelect", vParams, commandType: CommandType.StoredProcedure).FirstOrDefault();
    }

    public override void Insert(SvcToken aLoginSvcToken)
    {
        using var vConn = GetOpenConnection();
        var vParams = new DynamicParameters();
        vParams.Add("@AppUserId", aLoginSvcToken.AppUserId);
        vParams.Add("@LoginToken", aLoginSvcToken.LoginToken);
        vParams.Add("@TokenStatus", aLoginSvcToken.TokenStatus);
        vParams.Add("@ExipryDate", aLoginSvcToken.ExipryDate);
        vParams.Add("@IssueDate", aLoginSvcToken.IssueDate);
        vConn.Execute("TokenInsert", vParams, commandType: CommandType.StoredProcedure);
    }

    public override long InsertToGetId(SvcToken entity)
    {
        throw new NotImplementedException();
    }

    public override void Update(SvcToken aLoginSvcToken)
    {
        using var vConn = GetOpenConnection();
        var vParams = new DynamicParameters();
        vParams.Add("@LoginTokenId", aLoginSvcToken.SvcTokenId);
        vParams.Add("@AppUserId", aLoginSvcToken.AppUserId);
        vParams.Add("@LoginToken", aLoginSvcToken.LoginToken);
        vParams.Add("@TokenStatus", aLoginSvcToken.TokenStatus);
        vParams.Add("@ExipryDate", aLoginSvcToken.ExipryDate);
        vParams.Add("@IssueDate", aLoginSvcToken.IssueDate);
        vConn.Execute("LoginSvcTokenUpdate", vParams, commandType: CommandType.StoredProcedure);
    }
}
