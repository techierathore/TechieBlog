namespace BlogEngine.DbAccess;

/// <summary>
/// Repository for managing UserLogin data access operations using Dapper ORM.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides CRUD operations for the userlogins table in PostgreSQL.</para>
/// <para><b>Note:</b> PostgreSQL stores unquoted identifiers as lowercase.</para>
/// </remarks>
public class UserLoginRepo : GenericRepository<UserLogin>, IUserLoginRepository
{
    public UserLoginRepo(string connectionString) : base(connectionString) { }

    public override IEnumerable<UserLogin> GetAllById(long aSingleId)
    {
        using var vConn = GetOpenConnection();
        return vConn.Query<UserLogin>(
            "SELECT * FROM userlogins WHERE userid = @UserId ORDER BY logindate DESC",
            new { UserId = aSingleId });
    }

    public override IEnumerable<UserLogin> GetAll()
    {
        using var vConn = GetOpenConnection();
        return vConn.Query<UserLogin>("SELECT * FROM userlogins ORDER BY loginid");
    }

    public override UserLogin GetIntSingle(int aSingleId)
    {
        using var vConn = GetOpenConnection();
        return vConn.QueryFirstOrDefault<UserLogin>(
            "SELECT * FROM userlogins WHERE loginid = @LoginId",
            new { LoginId = aSingleId });
    }

    public UserLogin GetUserByToken(long aUserId, string aToken)
    {
        using var vConn = GetOpenConnection();
        return vConn.QueryFirstOrDefault<UserLogin>(
            "SELECT * FROM userlogins WHERE userid = @pUserId AND logintoken = @pLoginToken AND tokenstatus = 'ValidToken'",
            new { pUserId = aUserId, pLoginToken = aToken });
    }

    public override UserLogin GetSingle(long aSingleId)
    {
        using var vConn = GetOpenConnection();
        return vConn.QueryFirstOrDefault<UserLogin>(
            "SELECT * FROM userlogins WHERE loginid = @LoginId",
            new { LoginId = aSingleId });
    }

    public override long InsertToGetId(UserLogin aEntity)
    {
        using var vConn = GetOpenConnection();
        return vConn.ExecuteScalar<long>(
            @"INSERT INTO userlogins (userid, logindate, logintoken, tokenstatus, exiprydate, issuedate)
              VALUES (@UserId, @LoginDate, @LoginToken, @TokenStatus, @ExipryDate, @IssueDate)
              RETURNING loginid",
            new
            {
                aEntity.UserId,
                aEntity.LoginDate,
                aEntity.LoginToken,
                aEntity.TokenStatus,
                aEntity.ExipryDate,
                aEntity.IssueDate
            });
    }

    public override void Insert(UserLogin aEntity)
    {
        using var vConn = GetOpenConnection();
        vConn.Execute(
            @"INSERT INTO userlogins (userid, logindate, logintoken, tokenstatus, exiprydate, issuedate)
              VALUES (@UserId, @LoginDate, @LoginToken, @TokenStatus, @ExipryDate, @IssueDate)",
            new
            {
                aEntity.UserId,
                aEntity.LoginDate,
                aEntity.LoginToken,
                aEntity.TokenStatus,
                aEntity.ExipryDate,
                aEntity.IssueDate
            });
    }

    public override void Update(UserLogin aEntityToUpdate)
    {
        using var vConn = GetOpenConnection();
        vConn.Execute(
            @"UPDATE userlogins
              SET logindate = @LoginDate, logintoken = @LoginToken, tokenstatus = @TokenStatus,
                  exiprydate = @ExipryDate, issuedate = @IssueDate
              WHERE loginid = @LoginId",
            new
            {
                aEntityToUpdate.LoginId,
                aEntityToUpdate.LoginDate,
                aEntityToUpdate.LoginToken,
                aEntityToUpdate.TokenStatus,
                aEntityToUpdate.ExipryDate,
                aEntityToUpdate.IssueDate
            });
    }

    public override IEnumerable<UserLogin> GetPagedData(int PageSize, int OffSet)
    {
        using var vConn = GetOpenConnection();
        return vConn.Query<UserLogin>(
            "SELECT * FROM userlogins ORDER BY loginid LIMIT @PageSize OFFSET @OffSet",
            new { PageSize, OffSet });
    }
}
