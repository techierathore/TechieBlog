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

    /// <summary>
    /// Retrieves a user by their username (case-insensitive).
    /// </summary>
    /// <param name="username">The username to search for.</param>
    /// <returns>AppUser if found, null otherwise.</returns>
    public AppUser? GetByUsername(string username)
    {
        using var vConn = GetOpenConnection();
        return vConn.QueryFirstOrDefault<AppUser>(
            "SELECT * FROM BlogUser WHERE LOWER(Username) = LOWER(@Username)",
            new { Username = username });
    }

    /// <summary>
    /// Retrieves the site owner (user with IsSiteOwner=true).
    /// </summary>
    /// <returns>AppUser if found, null otherwise.</returns>
    public AppUser? GetSiteOwner()
    {
        using var vConn = GetOpenConnection();
        return vConn.QueryFirstOrDefault<AppUser>(
            "SELECT * FROM BlogUser WHERE IsSiteOwner = TRUE LIMIT 1");
    }

    /// <summary>
    /// Retrieves all users who have written at least one blog post.
    /// </summary>
    /// <returns>Collection of authors.</returns>
    public IEnumerable<AppUser> GetAllAuthors()
    {
        using var vConn = GetOpenConnection();
        return vConn.Query<AppUser>(
            "SELECT DISTINCT u.* FROM BlogUser u INNER JOIN BlogPost p ON u.UserId = p.UserID WHERE p.Published = true AND (p.IsDeleted = false OR p.IsDeleted IS NULL)");
    }

    /// <summary>
    /// Updates a user's username.
    /// </summary>
    /// <param name="userId">The user's ID.</param>
    /// <param name="username">The new username.</param>
    /// <returns>True if successful, false otherwise.</returns>
    public bool UpdateUsername(long userId, string username)
    {
        using var vConn = GetOpenConnection();
        var rowsAffected = vConn.Execute(
            "UPDATE BlogUser SET Username = @Username WHERE UserId = @UserId",
            new { Username = username, UserId = userId });
        return rowsAffected > 0;
    }

    /// <summary>
    /// Sets a user as the site owner, removing the flag from any previous owner.
    /// </summary>
    /// <param name="userId">The user ID to set as site owner.</param>
    /// <returns>True if successful, false otherwise.</returns>
    public bool SetSiteOwner(long userId)
    {
        using var vConn = GetOpenConnection();
        using var transaction = vConn.BeginTransaction();
        try
        {
            // Remove site owner flag from any previous owner
            vConn.Execute(
                "UPDATE BlogUser SET IsSiteOwner = FALSE WHERE IsSiteOwner = TRUE",
                transaction: transaction);

            // Set new site owner
            var rowsAffected = vConn.Execute(
                "UPDATE BlogUser SET IsSiteOwner = TRUE WHERE UserId = @UserId",
                new { UserId = userId },
                transaction: transaction);

            transaction.Commit();
            return rowsAffected > 0;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    /// <summary>
    /// Checks if a username is available (not already taken).
    /// </summary>
    /// <param name="username">The username to check.</param>
    /// <returns>True if available, false if taken.</returns>
    public bool IsUsernameAvailable(string username)
    {
        using var vConn = GetOpenConnection();
        var count = vConn.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM BlogUser WHERE LOWER(Username) = LOWER(@Username)",
            new { Username = username });
        return count == 0;
    }

    /// <summary>
    /// Updates only the resume-related fields for a user.
    /// </summary>
    /// <param name="userId">The user's ID.</param>
    /// <param name="resumeData">AppUser object containing resume field values.</param>
    /// <returns>True if successful, false otherwise.</returns>
    public bool UpdateResumeFields(long userId, AppUser resumeData)
    {
        using var vConn = GetOpenConnection();
        var rowsAffected = vConn.Execute(
            @"UPDATE BlogUser SET
                Title = @Title,
                Tagline = @Tagline,
                Location = @Location,
                PhoneNumber = @PhoneNumber,
                CVFilePath = @CVFilePath,
                ResumeEnabled = @ResumeEnabled,
                InstagramUrl = @InstagramUrl
              WHERE UserId = @UserId",
            new
            {
                UserId = userId,
                Title = resumeData.Title,
                Tagline = resumeData.Tagline,
                Location = resumeData.Location,
                PhoneNumber = resumeData.PhoneNumber,
                CVFilePath = resumeData.CVFilePath,
                ResumeEnabled = resumeData.ResumeEnabled,
                InstagramUrl = resumeData.InstagramUrl
            });
        return rowsAffected > 0;
    }
}
