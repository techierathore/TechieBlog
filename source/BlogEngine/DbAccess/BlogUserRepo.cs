using BlogModels.Models;

namespace BlogEngine.DbAccess;

/// <summary>
/// Repository for managing BlogUser data access operations using Dapper ORM.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides CRUD operations for the BlogUser table in PostgreSQL.</para>
/// <para><b>Code Flow:</b> Called by AuthSvc for authentication and user management.</para>
/// <para><b>Note:</b> PostgreSQL functions are called using SELECT * FROM syntax, not stored procedure calls.</para>
/// </remarks>
public class BlogUserRepo : GenericRepository<AppUser>, IBlogUserRepo
{
    public BlogUserRepo(string connectionString) : base(connectionString) { }

    /// <summary>
    /// Retrieves all users from the database.
    /// </summary>
    public override IEnumerable<AppUser> GetAll()
    {
        using var vConn = GetOpenConnection();
        return vConn.Query<AppUser>("SELECT * FROM BlogUser ORDER BY UserId");
    }

    /// <summary>
    /// Retrieves users matching the specified ID as a collection.
    /// </summary>
    /// <param name="aSingleId">The user ID to search for.</param>
    /// <returns>Collection containing the matching user, or empty if not found.</returns>
    public override IEnumerable<AppUser> GetAllById(long aSingleId)
    {
        using var vConn = GetOpenConnection();
        return vConn.Query<AppUser>(
            "SELECT * FROM BlogUser WHERE UserId = @UserId",
            new { UserId = aSingleId });
    }

    /// <summary>
    /// Retrieves a single user by their integer ID.
    /// </summary>
    /// <param name="aSingleId">The user's integer ID.</param>
    /// <returns>The user if found, null otherwise.</returns>
    public override AppUser GetIntSingle(int aSingleId)
    {
        return GetSingle((long)aSingleId);
    }

    /// <summary>
    /// Retrieves a single user by their ID using PostgreSQL function.
    /// </summary>
    public override AppUser GetSingle(long aSingleId)
    {
        using var vConn = GetOpenConnection();
        return vConn.QueryFirstOrDefault<AppUser>(
            "SELECT * FROM SelectBlogUserById(@pUserId)",
            new { pUserId = aSingleId });
    }

    /// <summary>
    /// Inserts a new user using PostgreSQL function.
    /// </summary>
    public override void Insert(AppUser aAppUser)
    {
        using var vConn = GetOpenConnection();
        vConn.Execute(
            "SELECT InsertBlogUser(@pFirstName, @pLastName, @pEmailId, @pLoginPass, @pUserRole)",
            new
            {
                pFirstName = aAppUser.FirstName,
                pLastName = aAppUser.LastName,
                pEmailId = aAppUser.EmailId,
                pLoginPass = aAppUser.LoginPass,
                pUserRole = aAppUser.UserRole
            });
    }

    /// <summary>
    /// Inserts a new user and returns the generated UserId.
    /// </summary>
    public override long InsertToGetId(AppUser aAppUser)
    {
        using var vConn = GetOpenConnection();
        return vConn.QuerySingle<long>(
            "SELECT InsertBlogUser(@pFirstName, @pLastName, @pEmailId, @pLoginPass, @pUserRole)",
            new
            {
                pFirstName = aAppUser.FirstName,
                pLastName = aAppUser.LastName,
                pEmailId = aAppUser.EmailId,
                pLoginPass = aAppUser.LoginPass,
                pUserRole = aAppUser.UserRole
            });
    }

    /// <summary>
    /// Updates an existing user using PostgreSQL function.
    /// </summary>
    public override void Update(AppUser aBlogUser)
    {
        using var vConn = GetOpenConnection();
        vConn.Execute(
            @"SELECT UpdateBlogUser(@pUserId, @pFirstName, @pLastName, @pEmailId, @pLoginPass,
              @pUserRole, @pProfileImagePath, @pProfileDescription, @pTwitterUrl,
              @pLinkedInUrl, @pGitHubUrl, @pPodDescription, @pSpeakDescription)",
            new
            {
                pUserId = aBlogUser.UserId,
                pFirstName = aBlogUser.FirstName,
                pLastName = aBlogUser.LastName,
                pEmailId = aBlogUser.EmailId,
                pLoginPass = aBlogUser.LoginPass,
                pUserRole = aBlogUser.UserRole,
                pProfileImagePath = aBlogUser.ProfileImagePath ?? string.Empty,
                pProfileDescription = aBlogUser.ProfileDescription ?? string.Empty,
                pTwitterUrl = aBlogUser.TwiiterUrl ?? string.Empty,
                pLinkedInUrl = aBlogUser.LinkedInUrl ?? string.Empty,
                pGitHubUrl = aBlogUser.GitHubUrl ?? string.Empty,
                pPodDescription = aBlogUser.PodDescription ?? string.Empty,
                pSpeakDescription = aBlogUser.SpeakDescription ?? string.Empty
            });
    }

    /// <summary>
    /// Authenticates a user with email and password using PostgreSQL function.
    /// </summary>
    public AppUser GetLoginUser(string aLoginEmail, string aPassword)
    {
        using var vConn = GetOpenConnection();
        return vConn.QueryFirstOrDefault<AppUser>(
            "SELECT * FROM GetLoginUser(@pLoginMail, @pLoginPassword)",
            new { pLoginMail = aLoginEmail, pLoginPassword = aPassword });
    }

    /// <summary>
    /// Retrieves a user by email address using PostgreSQL function.
    /// </summary>
    public AppUser GetUserByEmail(string aLoginEmail)
    {
        using var vConn = GetOpenConnection();
        return vConn.QueryFirstOrDefault<AppUser>(
            "SELECT * FROM GetUserByEmail(@pLoginMail)",
            new { pLoginMail = aLoginEmail });
    }

    /// <summary>
    /// Retrieves a user by mobile number.
    /// </summary>
    public AppUser GetUserByMobile(string aMobileNo)
    {
        using var vConn = GetOpenConnection();
        return vConn.QueryFirstOrDefault<AppUser>(
            "SELECT * FROM BlogUser WHERE MobileNo = @MobileNo",
            new { MobileNo = aMobileNo });
    }

    /// <summary>
    /// Retrieves a paginated list of users.
    /// </summary>
    /// <param name="PageSize">Number of users per page.</param>
    /// <param name="OffSet">Number of users to skip.</param>
    /// <returns>Paginated collection of users.</returns>
    public override IEnumerable<AppUser> GetPagedData(int PageSize, int OffSet)
    {
        using var vConn = GetOpenConnection();
        return vConn.Query<AppUser>(
            "SELECT * FROM BlogUser ORDER BY UserId LIMIT @PageSize OFFSET @OffSet",
            new { PageSize, OffSet });
    }
}
