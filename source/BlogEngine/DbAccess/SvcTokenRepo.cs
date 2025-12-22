namespace BlogEngine.DbAccess;

/// <summary>
/// Repository for managing SvcToken data access operations using Dapper ORM.
/// Note: This repo may be deprecated - consider using UserLoginRepo for token management.
/// </summary>
public class SvcTokenRepo : GenericRepository<SvcToken>, ISvcTokenRepo
{
    public SvcTokenRepo(string connectionString) : base(connectionString) { }

    public override IEnumerable<SvcToken> GetAll()
    {
        using var vConn = GetOpenConnection();
        return vConn.Query<SvcToken>("SELECT * FROM svctoken ORDER BY issuedate DESC");
    }

    public override IEnumerable<SvcToken> GetAllById(long aSingleId)
    {
        using var vConn = GetOpenConnection();
        return vConn.Query<SvcToken>(
            "SELECT * FROM svctoken WHERE appuserid = @UserId",
            new { UserId = aSingleId });
    }

    public override SvcToken GetIntSingle(int aOrgId)
    {
        throw new NotImplementedException();
    }

    public override IEnumerable<SvcToken> GetPagedData(int PageSize, int OffSet)
    {
        using var vConn = GetOpenConnection();
        return vConn.Query<SvcToken>(
            @"SELECT * FROM svctoken ORDER BY issuedate DESC LIMIT @PageSize OFFSET @OffSet",
            new { PageSize, OffSet });
    }

    public override SvcToken GetSingle(long aSingleId)
    {
        using var vConn = GetOpenConnection();
        return vConn.QueryFirstOrDefault<SvcToken>(
            "SELECT * FROM svctoken WHERE svctokenid = @TokenId",
            new { TokenId = aSingleId });
    }

    public SvcToken GetSvcToken(long aAppUserId, string aLoginToken)
    {
        using var vConn = GetOpenConnection();
        return vConn.QueryFirstOrDefault<SvcToken>(
            "SELECT * FROM svctoken WHERE appuserid = @UserId AND logintoken = @Token AND tokenstatus = 'ValidToken'",
            new { UserId = aAppUserId, Token = aLoginToken });
    }

    public override void Insert(SvcToken aLoginSvcToken)
    {
        using var vConn = GetOpenConnection();
        vConn.Execute(
            @"INSERT INTO svctoken (appuserid, logintoken, tokenstatus, exiprydate, issuedate)
              VALUES (@AppUserId, @LoginToken, @TokenStatus, @ExipryDate, @IssueDate)",
            new
            {
                aLoginSvcToken.AppUserId,
                aLoginSvcToken.LoginToken,
                aLoginSvcToken.TokenStatus,
                aLoginSvcToken.ExipryDate,
                aLoginSvcToken.IssueDate
            });
    }

    public override long InsertToGetId(SvcToken entity)
    {
        throw new NotImplementedException();
    }

    public override void Update(SvcToken aLoginSvcToken)
    {
        using var vConn = GetOpenConnection();
        vConn.Execute(
            @"UPDATE svctoken
              SET logintoken = @LoginToken, tokenstatus = @TokenStatus,
                  exiprydate = @ExipryDate, issuedate = @IssueDate
              WHERE svctokenid = @SvcTokenId",
            new
            {
                aLoginSvcToken.SvcTokenId,
                aLoginSvcToken.LoginToken,
                aLoginSvcToken.TokenStatus,
                aLoginSvcToken.ExipryDate,
                aLoginSvcToken.IssueDate
            });
    }
}
