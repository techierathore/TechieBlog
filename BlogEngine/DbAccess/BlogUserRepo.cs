namespace BlogEngine.DbAccess;

public class BlogUserRepo : GenericRepository<BlogUser>, IBlogUserRepo
{
    public BlogUserRepo(string connectionString) : base(connectionString) { }

    public override IEnumerable<BlogUser> GetAll()
    {
        using var vConn = GetOpenConnection();
        return vConn.Query<BlogUser>("BlogUsersSelectAll", commandType: CommandType.StoredProcedure);
    }
    public override IEnumerable<BlogUser> GetAllById(long aSingleId)
    {
        throw new System.NotImplementedException();
    }
    public override BlogUser GetIntSingle(int aSingleId)
    {
        throw new System.NotImplementedException();
    }

    public override BlogUser GetSingle(long aSingleId)
    {
        using var vConn = GetOpenConnection();
        var vParams = new DynamicParameters();
        vParams.Add("@UserId", aSingleId);
        return vConn.QueryFirstOrDefault<BlogUser>("AppUserSelect", vParams, commandType: CommandType.StoredProcedure);
    }

    public override void Insert(BlogUser aAppUser)
    {
        using var vConn = GetOpenConnection();
        var vParams = new DynamicParameters();
        vParams.Add("@FirstName", aAppUser.FirstName);
        vParams.Add("@LastName", aAppUser.LastName);
        vParams.Add("@EmailID", aAppUser.EmailId);
        vParams.Add("@loginPassword", aAppUser.LoginPass);
        vParams.Add("@Role", aAppUser.UserRole);
        vParams.Add("@CreatedTime", aAppUser.CreatedTime);
        vParams.Add("@UpdatedTime", aAppUser.UpdatedTime);
        int iResult = vConn.Execute("BlogUsersInsert", vParams, commandType: CommandType.StoredProcedure);
    }

    public override long InsertToGetId(BlogUser aAppUser)
    {
        long lLastInsertedId = 0;
        using var vConn = GetOpenConnection();
        var vParams = new DynamicParameters();
        vParams.Add("@FirstName", aAppUser.FirstName);
        vParams.Add("@LastName", aAppUser.LastName);
        vParams.Add("@EmailID", aAppUser.EmailId);
        vParams.Add("@loginPassword", aAppUser.LoginPass);
        vParams.Add("@Role", aAppUser.UserRole);
        vParams.Add("@CreatedTime", aAppUser.CreatedTime);
        vParams.Add("@UpdatedTime", aAppUser.UpdatedTime);
        vParams.Add("@pInsertedId", lLastInsertedId, direction: ParameterDirection.Output);
        int iResult = vConn.Execute("AppUserInsert4Id", vParams, commandType: CommandType.StoredProcedure);
        lLastInsertedId = vParams.Get<long>("@pInsertedId");
        return lLastInsertedId;
    }

    public override void Update(BlogUser aBlogUser)
    {
        using var vConn = GetOpenConnection();
        var vParams = new DynamicParameters();
        vParams.Add("@UserID", aBlogUser.UserId);
        vParams.Add("@FirstName", aBlogUser.FirstName);
        vParams.Add("@LastName", aBlogUser.LastName);
        vParams.Add("@EmailID", aBlogUser.EmailId);
        vParams.Add("@loginPassword", aBlogUser.LoginPass);
        vParams.Add("@Role", aBlogUser.UserRole);
        vParams.Add("@CreatedTime", aBlogUser.CreatedTime);
        vParams.Add("@UpdatedTime", aBlogUser.UpdatedTime);
        vParams.Add("@LastLogin", aBlogUser.LastLogin);
        vConn.Execute("BlogUsersUpdate", vParams, commandType: CommandType.StoredProcedure);
    }

    public BlogUser GetLoginUser(string aLoginEmail, string aPassword)
    {
        using var vConn = GetOpenConnection();
        var vParams = new DynamicParameters();
        vParams.Add("@LoginMail", aLoginEmail);
        vParams.Add("@LoginPassword", aPassword);
        var vReturnUser = vConn.Query<BlogUser>("GetLoginUser", vParams, commandType: CommandType.StoredProcedure).SingleOrDefault();
        return vReturnUser;
    }

    public BlogUser GetUserByEmail(string aLoginEmail)
    {
        using var vConn = GetOpenConnection();
        var vReturnUser = vConn.Query<BlogUser>("GetUserByEmail", new { LoginMail = aLoginEmail }, commandType: CommandType.StoredProcedure).SingleOrDefault();
        return vReturnUser;
    }

    public BlogUser GetUserByMobile(string aMobileNo)
    {
        using var vConn = GetOpenConnection();
        var vReturnUser = vConn.Query<BlogUser>("GetUserByMobile", new { UserMobileNo = aMobileNo }, commandType: CommandType.StoredProcedure).SingleOrDefault();
        return vReturnUser;
    }

    public override IEnumerable<BlogUser> GetPagedData(int PageSize, int OffSet)
    {
        throw new NotImplementedException();
    }
}
