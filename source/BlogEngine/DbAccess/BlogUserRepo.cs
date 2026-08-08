using BlogModels.Models;

namespace BlogEngine.DbAccess;

/// <summary>
/// Repository for managing BlogUser data access operations using Dapper ORM.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides CRUD operations for the BlogUser table in PostgreSQL.</para>
///
/// <para><b>Code Flow:</b> Called by <c>AuthSvc</c> for authentication and user management, and by
/// the admin/profile pages for the user grid, the site-owner lookup and the résumé fields.</para>
///
/// <para><b>Dependencies:</b> GenericRepository, Dapper, PostgreSQL.</para>
///
/// <para><b>Usage:</b> Registered by <c>BlogSvcInitializer</c> as <c>IBlogUserRepo</c>. Call the
/// <c>…Async</c> members. The password hash is <b>not</b> this repository's business — reading and
/// rotating <c>LoginPass</c> belongs to <c>UserCredentialRepo</c>, which exists precisely so the hash
/// stays out of the wide projections below and so a password change cannot clobber a profile.</para>
///
/// <para><b>Note:</b> PostgreSQL functions are called using SELECT * FROM syntax, not stored
/// procedure calls. This repository is a mixture: identity and mutation go through the stored
/// functions <c>SelectBlogUserById</c>, <c>InsertBlogUser</c>, <c>UpdateBlogUser</c>,
/// <c>GetLoginUser</c> and <c>GetUserByEmail</c>, while the grid reads, the username, site-owner and
/// résumé statements are inline SQL against <c>BlogUser</c>.</para>
///
/// <para><b>Projection:</b> the inline reads are <c>SELECT *</c>, so they track the table and return
/// every column <c>AppUser</c> can bind. The stored-function reads return whatever the function's
/// <c>RETURNS TABLE</c> clause declares, which is <b>not</b> guaranteed to be the whole row — a
/// column added to <c>BlogUser</c> by a later migration appears in the inline reads immediately and
/// in the function reads only when the function is rewritten. That asymmetry is the thing to check
/// first when a field is populated on one page and empty on another.</para>
///
/// <para><b>Async conversion (REQ-NFR-026):</b> every member exists twice. The <c>…Async</c> members
/// are the surface callers should use — they open the connection asynchronously through the
/// protected helpers, flow the <c>CancellationToken</c> into the Dapper command and never park a
/// thread-pool thread. The synchronous twins are retained only until the last caller migrates and
/// execute the same SQL constants, so the two cannot drift apart.</para>
/// </remarks>
public class BlogUserRepo : GenericRepository<AppUser>, IBlogUserRepo
{
    private const string SelectAllSql = "SELECT * FROM BlogUser ORDER BY UserId";

    private const string SelectByIdListSql = "SELECT * FROM BlogUser WHERE UserId = @UserId";

    private const string SelectByIdSql = "SELECT * FROM SelectBlogUserById(@pUserId)";

    private const string InsertSql =
        "SELECT InsertBlogUser(@pFirstName, @pLastName, @pEmailId, @pLoginPass, @pUserRole)";

    private const string UpdateSql = @"
            SELECT UpdateBlogUser(@pUserId, @pFirstName, @pLastName, @pEmailId, @pLoginPass,
              @pUserRole, @pProfileImagePath, @pProfileDescription, @pTwitterUrl,
              @pLinkedInUrl, @pGitHubUrl, @pPodDescription, @pSpeakDescription)";

    private const string SelectLoginUserSql = "SELECT * FROM GetLoginUser(@pLoginMail, @pLoginPassword)";

    private const string SelectByEmailSql = "SELECT * FROM GetUserByEmail(@pLoginMail)";

    private const string SelectByMobileSql = "SELECT * FROM BlogUser WHERE MobileNo = @MobileNo";

    private const string SelectPagedSql =
        "SELECT * FROM BlogUser ORDER BY UserId LIMIT @PageSize OFFSET @OffSet";

    private const string SelectByUsernameSql =
        "SELECT * FROM BlogUser WHERE LOWER(Username) = LOWER(@Username)";

    private const string SelectSiteOwnerSql = "SELECT * FROM BlogUser WHERE IsSiteOwner = TRUE LIMIT 1";

    private const string SelectAuthorsSql = @"
            SELECT DISTINCT u.* FROM BlogUser u
            INNER JOIN BlogPost p ON u.UserId = p.UserID
            WHERE p.Published = true AND (p.IsDeleted = false OR p.IsDeleted IS NULL)";

    private const string UpdateUsernameSql =
        "UPDATE BlogUser SET Username = @Username WHERE UserId = @UserId";

    private const string ClearSiteOwnerSql =
        "UPDATE BlogUser SET IsSiteOwner = FALSE WHERE IsSiteOwner = TRUE";

    private const string SetSiteOwnerSql =
        "UPDATE BlogUser SET IsSiteOwner = TRUE WHERE UserId = @UserId";

    private const string CountByUsernameSql =
        "SELECT COUNT(*) FROM BlogUser WHERE LOWER(Username) = LOWER(@Username)";

    private const string UpdateResumeFieldsSql = @"
            UPDATE BlogUser SET
                Title = @Title,
                Tagline = @Tagline,
                Location = @Location,
                PhoneNumber = @PhoneNumber,
                CVFilePath = @CVFilePath,
                ResumeEnabled = @ResumeEnabled,
                InstagramUrl = @InstagramUrl
              WHERE UserId = @UserId";

    /// <summary>
    /// Initialises the repository with the application connection string.
    /// </summary>
    /// <param name="connectionString">PostgreSQL connection string from <c>AppDbConString</c>.</param>
    public BlogUserRepo(string connectionString) : base(connectionString) { }

    // =================================================================================================
    // Async surface — the members every caller should use.
    // =================================================================================================

    /// <summary>
    /// Retrieves all users, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Ordered by identifier so the admin grid keeps a stable sequence
    /// between refreshes.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → buffered query.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>All users, or an empty sequence when the table holds no rows.</returns>
    public override async Task<IEnumerable<AppUser>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await QueryAsync<AppUser>(SelectAllSql, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves users matching the specified ID as a collection, without blocking the caller.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The identifier is the primary key, so the result holds at most
    /// one row; the collection shape exists only to satisfy the generic contract.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → buffered query by key.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="userId">The user ID to search for.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The matching user, or an empty sequence when the key is unknown.</returns>
    public override async Task<IEnumerable<AppUser>> GetAllByIdAsync(long userId, CancellationToken cancellationToken = default)
    {
        return await QueryAsync<AppUser>(
            SelectByIdListSql, new { UserId = userId }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves a single user by their integer ID, without blocking the caller.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Widens the key and reuses the BIGINT lookup; the column is
    /// <c>BIGINT</c>, so there is no second query to write.</para>
    /// <para><b>Flow:</b> widen → delegate to <see cref="GetSingleAsync"/>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="userId">The user's integer ID.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The user, or <c>null</c> when the key is unknown.</returns>
    public override Task<AppUser?> GetIntSingleAsync(int userId, CancellationToken cancellationToken = default)
    {
        return GetSingleAsync(userId, cancellationToken);
    }

    /// <summary>
    /// Retrieves a single user by their ID, without blocking the caller.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> An unknown identifier is a normal answer and yields <c>null</c>,
    /// which is what lets a stale JWT resolve to "signed out" rather than to an error.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → <c>SelectBlogUserById</c>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="userId">The user's identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The user, or <c>null</c> when the key is unknown.</returns>
    public override async Task<AppUser?> GetSingleAsync(long userId, CancellationToken cancellationToken = default)
    {
        return await QueryFirstOrDefaultAsync<AppUser>(
            SelectByIdSql, new { pUserId = userId }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Inserts a new user, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The caller does not need the generated key here, so the function
    /// result is discarded rather than read back.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → call <c>InsertBlogUser</c>.</para>
    /// <para><b>Side Effects:</b> Adds one row to <c>BlogUser</c>.</para>
    /// </remarks>
    /// <param name="appUser">The user to persist.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    public override async Task InsertAsync(AppUser appUser, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(InsertSql, BuildInsertParameters(appUser), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Inserts a new user and returns the generated UserId, without blocking the caller.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> <c>InsertBlogUser</c> returns the identity, so the key arrives
    /// without a second round trip. Zero rows would mean the function did not run at all, which is a
    /// schema error rather than a normal answer — hence <c>QuerySingleAsync</c> rather than a scalar
    /// read that would quietly return <c>0</c>.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → call <c>InsertBlogUser</c> →
    /// read the returned key.</para>
    /// <para><b>Side Effects:</b> Adds one row to <c>BlogUser</c>.</para>
    /// </remarks>
    /// <param name="appUser">The user to persist.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>The generated <c>UserId</c>.</returns>
    public override async Task<long> InsertToGetIdAsync(AppUser appUser, CancellationToken cancellationToken = default)
    {
        return await QuerySingleAsync<long>(
            InsertSql, BuildInsertParameters(appUser), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Updates an existing user, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> <c>UpdateBlogUser</c> does not accept nulls, so every optional
    /// profile field is coalesced to an empty string before it is bound.</para>
    /// <para><b>Flow:</b> bind parameters → helper opens the connection asynchronously → call
    /// <c>UpdateBlogUser</c>.</para>
    /// <para><b>Side Effects:</b> Updates one <c>BlogUser</c> row.</para>
    /// </remarks>
    /// <param name="blogUser">The user carrying the new values.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    public override async Task UpdateAsync(AppUser blogUser, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(UpdateSql, BuildUpdateParameters(blogUser), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Authenticates a user with email and password, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Legacy single-query sign-in retained for compatibility; the live
    /// login path verifies a PBKDF2 hash through <c>IUserCredentialRepo</c> instead.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → call <c>GetLoginUser</c>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="loginEmail">The account's email address.</param>
    /// <param name="password">The password to match.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The matching user, or <c>null</c> when the pair does not match an account.</returns>
    public async Task<AppUser?> GetLoginUserAsync(string loginEmail, string password, CancellationToken cancellationToken = default)
    {
        return await QueryFirstOrDefaultAsync<AppUser>(
            SelectLoginUserSql,
            new { pLoginMail = loginEmail, pLoginPassword = password },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves a user by email address, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The address is the account's natural key, so this backs both the
    /// password-reset lookup and the duplicate-account check. An unknown address yields <c>null</c>,
    /// which the reset flow deliberately treats as success (account-enumeration defence).</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → call <c>GetUserByEmail</c>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="loginEmail">The address to search for.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The matching user, or <c>null</c> when no account uses that address.</returns>
    public async Task<AppUser?> GetUserByEmailAsync(string loginEmail, CancellationToken cancellationToken = default)
    {
        return await QueryFirstOrDefaultAsync<AppUser>(
            SelectByEmailSql, new { pLoginMail = loginEmail }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves a user by mobile number, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Inline <c>SELECT * FROM BlogUser WHERE MobileNo = @MobileNo</c> —
    /// an exact string comparison, unlike the email and username lookups, which are
    /// case-insensitive. There is no normalisation of country codes, spacing or punctuation here, so
    /// two spellings of the same number do not match; any normalisation must happen before the call.
    /// The column carries no uniqueness constraint either, so if two accounts share a number this
    /// returns whichever row the planner yields first.</para>
    /// <para><b>Flow:</b> bind the number → helper opens the connection asynchronously → first row or
    /// <c>null</c>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="mobileNo">The mobile number to search for.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The matching user, or <c>null</c> when no account uses that number.</returns>
    public async Task<AppUser?> GetUserByMobileAsync(string mobileNo, CancellationToken cancellationToken = default)
    {
        return await QueryFirstOrDefaultAsync<AppUser>(
            SelectByMobileSql, new { MobileNo = mobileNo }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves a page of users, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The paged form of <see cref="GetAllAsync"/>, sharing its
    /// <c>ORDER BY UserId</c> so a page boundary means the same thing in both. Ordering on the key
    /// rather than a name gives a total order that cannot tie, which is what stops a row appearing on
    /// two pages or on none as the grid is paged. Paging is applied in SQL, so a large user table
    /// never crosses the wire in full.</para>
    /// <para><b>Projection:</b> <c>SELECT *</c> against <c>BlogUser</c>, so the page carries every
    /// column the entity binds — including <c>LoginPass</c>. Callers must not surface it; the
    /// narrow, safe read of that column is <c>UserCredentialRepo</c>.</para>
    /// <para><b>Flow:</b> bind the window → helper opens the connection asynchronously →
    /// LIMIT/OFFSET query → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="pageSize">Number of users per page.</param>
    /// <param name="offSet">Number of users to skip.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The requested page, or an empty sequence when the offset is past the end.</returns>
    public override async Task<IEnumerable<AppUser>> GetPagedDataAsync(int pageSize, int offSet, CancellationToken cancellationToken = default)
    {
        return await QueryAsync<AppUser>(
            SelectPagedSql, new { PageSize = pageSize, OffSet = offSet }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves a user by their username, case-insensitively, without blocking the caller.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Usernames address a public profile URL, so they are matched
    /// without regard to case.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → lowercased comparison.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="username">The username to search for.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The matching user, or <c>null</c> when the username is unknown.</returns>
    public async Task<AppUser?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        return await QueryFirstOrDefaultAsync<AppUser>(
            SelectByUsernameSql, new { Username = username }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves the site owner, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> At most one account carries the flag; the <c>LIMIT 1</c> keeps a
    /// mis-seeded database from returning two owners to a page that can only render one.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → flag query.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The site owner, or <c>null</c> when none is flagged.</returns>
    public async Task<AppUser?> GetSiteOwnerAsync(CancellationToken cancellationToken = default)
    {
        return await QueryFirstOrDefaultAsync<AppUser>(
            SelectSiteOwnerSql, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves every user who has written at least one published post, without blocking the caller.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Drafts and soft-deleted posts do not make someone an author, so
    /// the join filters both out before the distinct set is taken.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → distinct inner join.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The authors, or an empty sequence when nobody has published.</returns>
    public async Task<IEnumerable<AppUser>> GetAllAuthorsAsync(CancellationToken cancellationToken = default)
    {
        return await QueryAsync<AppUser>(SelectAuthorsSql, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Updates a user's username, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Reported as a boolean rather than a row count because the caller
    /// only needs to know whether the name was taken up.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → UPDATE → compare row count.</para>
    /// <para><b>Side Effects:</b> Updates one <c>BlogUser</c> row.</para>
    /// </remarks>
    /// <param name="userId">The user's ID.</param>
    /// <param name="username">The new username.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns><c>true</c> when a row was updated.</returns>
    public async Task<bool> UpdateUsernameAsync(long userId, string username, CancellationToken cancellationToken = default)
    {
        var rowsAffected = await ExecuteAsync(
            UpdateUsernameSql,
            new { Username = username, UserId = userId },
            cancellationToken).ConfigureAwait(false);

        return rowsAffected > 0;
    }

    /// <summary>
    /// Sets a user as the site owner, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Clearing the previous owner and flagging the new one must happen
    /// together — a failure between the two statements would leave the site with no owner and blank
    /// the landing page — so both run inside one transaction.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → begin transaction → clear → set →
    /// commit, or roll back and rethrow.</para>
    /// <para><b>Side Effects:</b> Updates up to two <c>BlogUser</c> rows.</para>
    /// </remarks>
    /// <param name="userId">The user ID to set as site owner.</param>
    /// <param name="cancellationToken">Cancels the transaction.</param>
    /// <returns><c>true</c> when the new owner was flagged.</returns>
    public async Task<bool> SetSiteOwnerAsync(long userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                ClearSiteOwnerSql, transaction: transaction, cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            var rowsAffected = await connection.ExecuteAsync(new CommandDefinition(
                SetSiteOwnerSql, new { UserId = userId }, transaction, cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return rowsAffected > 0;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Checks whether a username is still free, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Availability is the inverse of existence, compared
    /// case-insensitively so two names differing only in case cannot both be taken.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → counting query → compare.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="username">The username to check.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns><c>true</c> when no account already uses the name.</returns>
    public async Task<bool> IsUsernameAvailableAsync(string username, CancellationToken cancellationToken = default)
    {
        var matches = await ExecuteScalarAsync<int>(
            CountByUsernameSql, new { Username = username }, cancellationToken).ConfigureAwait(false);

        return matches == 0;
    }

    /// <summary>
    /// Updates only the resume-related fields for a user, without blocking the caller.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The résumé columns are written on their own so saving the
    /// portfolio cannot overwrite the profile fields the form did not load.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → UPDATE → compare row count.</para>
    /// <para><b>Side Effects:</b> Updates one <c>BlogUser</c> row.</para>
    /// </remarks>
    /// <param name="userId">The user's ID.</param>
    /// <param name="resumeData">AppUser object containing resume field values.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns><c>true</c> when a row was updated.</returns>
    public async Task<bool> UpdateResumeFieldsAsync(long userId, AppUser resumeData, CancellationToken cancellationToken = default)
    {
        var rowsAffected = await ExecuteAsync(
            UpdateResumeFieldsSql,
            BuildResumeParameters(userId, resumeData),
            cancellationToken).ConfigureAwait(false);

        return rowsAffected > 0;
    }

    // =================================================================================================
    // Legacy blocking surface — REQ-NFR-026 deletes these once every caller has migrated.
    // Each one executes the same SQL constant as its async twin, so the two cannot drift.
    // =================================================================================================

    /// <summary>
    /// Retrieves all users from the database.
    /// </summary>
    /// <returns>All users.</returns>
    public override IEnumerable<AppUser> GetAll()
    {
        using var connection = GetOpenConnection();
        return connection.Query<AppUser>(SelectAllSql).ToList();
    }

    /// <summary>
    /// Retrieves users matching the specified ID as a collection.
    /// </summary>
    /// <param name="userId">The user ID to search for.</param>
    /// <returns>Collection containing the matching user, or empty if not found.</returns>
    public override IEnumerable<AppUser> GetAllById(long userId)
    {
        using var connection = GetOpenConnection();
        return connection.Query<AppUser>(SelectByIdListSql, new { UserId = userId }).ToList();
    }

    /// <summary>
    /// Retrieves a single user by their integer ID.
    /// </summary>
    /// <param name="userId">The user's integer ID.</param>
    /// <returns>The user if found, null otherwise.</returns>
    public override AppUser? GetIntSingle(int userId)
    {
        return GetSingle(userId);
    }

    /// <summary>
    /// Retrieves a single user by their ID using a PostgreSQL function.
    /// </summary>
    /// <param name="userId">The user's identifier.</param>
    /// <returns>The user if found, null otherwise.</returns>
    public override AppUser? GetSingle(long userId)
    {
        using var connection = GetOpenConnection();
        return connection.QueryFirstOrDefault<AppUser>(SelectByIdSql, new { pUserId = userId });
    }

    /// <summary>
    /// Inserts a new user using a PostgreSQL function.
    /// </summary>
    /// <param name="appUser">The user to persist.</param>
    public override void Insert(AppUser appUser)
    {
        using var connection = GetOpenConnection();
        connection.Execute(InsertSql, BuildInsertParameters(appUser));
    }

    /// <summary>
    /// Inserts a new user and returns the generated UserId.
    /// </summary>
    /// <param name="appUser">The user to persist.</param>
    /// <returns>The generated <c>UserId</c>.</returns>
    public override long InsertToGetId(AppUser appUser)
    {
        using var connection = GetOpenConnection();
        return connection.QuerySingle<long>(InsertSql, BuildInsertParameters(appUser));
    }

    /// <summary>
    /// Updates an existing user using a PostgreSQL function.
    /// </summary>
    /// <param name="blogUser">The user carrying the new values.</param>
    public override void Update(AppUser blogUser)
    {
        using var connection = GetOpenConnection();
        connection.Execute(UpdateSql, BuildUpdateParameters(blogUser));
    }

    /// <summary>
    /// Authenticates a user with email and password using a PostgreSQL function.
    /// </summary>
    /// <param name="loginEmail">The account's email address.</param>
    /// <param name="password">The password to match.</param>
    /// <returns>The matching user, or <c>null</c>.</returns>
    public AppUser? GetLoginUser(string loginEmail, string password)
    {
        using var connection = GetOpenConnection();
        return connection.QueryFirstOrDefault<AppUser>(
            SelectLoginUserSql, new { pLoginMail = loginEmail, pLoginPassword = password });
    }

    /// <summary>
    /// Retrieves a user by email address using a PostgreSQL function.
    /// </summary>
    /// <param name="loginEmail">The address to search for.</param>
    /// <returns>The matching user, or <c>null</c>.</returns>
    public AppUser? GetUserByEmail(string loginEmail)
    {
        using var connection = GetOpenConnection();
        return connection.QueryFirstOrDefault<AppUser>(SelectByEmailSql, new { pLoginMail = loginEmail });
    }

    /// <summary>
    /// Retrieves a user by mobile number.
    /// </summary>
    /// <param name="mobileNo">The mobile number to search for.</param>
    /// <returns>The matching user, or <c>null</c>.</returns>
    public AppUser? GetUserByMobile(string mobileNo)
    {
        using var connection = GetOpenConnection();
        return connection.QueryFirstOrDefault<AppUser>(SelectByMobileSql, new { MobileNo = mobileNo });
    }

    /// <summary>
    /// Retrieves a paginated list of users.
    /// </summary>
    /// <param name="pageSize">Number of users per page.</param>
    /// <param name="offSet">Number of users to skip.</param>
    /// <returns>Paginated collection of users.</returns>
    public override IEnumerable<AppUser> GetPagedData(int pageSize, int offSet)
    {
        using var connection = GetOpenConnection();
        return connection.Query<AppUser>(SelectPagedSql, new { PageSize = pageSize, OffSet = offSet }).ToList();
    }

    /// <summary>
    /// Retrieves a user by their username (case-insensitive).
    /// </summary>
    /// <param name="username">The username to search for.</param>
    /// <returns>AppUser if found, null otherwise.</returns>
    public AppUser? GetByUsername(string username)
    {
        using var connection = GetOpenConnection();
        return connection.QueryFirstOrDefault<AppUser>(SelectByUsernameSql, new { Username = username });
    }

    /// <summary>
    /// Retrieves the site owner (user with IsSiteOwner=true).
    /// </summary>
    /// <returns>AppUser if found, null otherwise.</returns>
    public AppUser? GetSiteOwner()
    {
        using var connection = GetOpenConnection();
        return connection.QueryFirstOrDefault<AppUser>(SelectSiteOwnerSql);
    }

    /// <summary>
    /// Retrieves all users who have written at least one blog post.
    /// </summary>
    /// <returns>Collection of authors.</returns>
    public IEnumerable<AppUser> GetAllAuthors()
    {
        using var connection = GetOpenConnection();
        return connection.Query<AppUser>(SelectAuthorsSql).ToList();
    }

    /// <summary>
    /// Updates a user's username.
    /// </summary>
    /// <param name="userId">The user's ID.</param>
    /// <param name="username">The new username.</param>
    /// <returns>True if successful, false otherwise.</returns>
    public bool UpdateUsername(long userId, string username)
    {
        using var connection = GetOpenConnection();
        return connection.Execute(UpdateUsernameSql, new { Username = username, UserId = userId }) > 0;
    }

    /// <summary>
    /// Sets a user as the site owner, removing the flag from any previous owner.
    /// </summary>
    /// <param name="userId">The user ID to set as site owner.</param>
    /// <returns>True if successful, false otherwise.</returns>
    public bool SetSiteOwner(long userId)
    {
        using var connection = GetOpenConnection();
        using var transaction = connection.BeginTransaction();
        try
        {
            connection.Execute(ClearSiteOwnerSql, transaction: transaction);
            var rowsAffected = connection.Execute(
                SetSiteOwnerSql, new { UserId = userId }, transaction: transaction);

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
        using var connection = GetOpenConnection();
        return connection.ExecuteScalar<int>(CountByUsernameSql, new { Username = username }) == 0;
    }

    /// <summary>
    /// Updates only the resume-related fields for a user.
    /// </summary>
    /// <param name="userId">The user's ID.</param>
    /// <param name="resumeData">AppUser object containing resume field values.</param>
    /// <returns>True if successful, false otherwise.</returns>
    public bool UpdateResumeFields(long userId, AppUser resumeData)
    {
        using var connection = GetOpenConnection();
        return connection.Execute(UpdateResumeFieldsSql, BuildResumeParameters(userId, resumeData)) > 0;
    }

    // =================================================================================================
    // Parameter builders — shared by both twins so the bound columns cannot drift either.
    // =================================================================================================

    /// <summary>
    /// Builds the parameter set for <c>InsertBlogUser</c>.
    /// </summary>
    /// <param name="appUser">The user being inserted.</param>
    /// <returns>The anonymous parameter object Dapper binds.</returns>
    private static object BuildInsertParameters(AppUser appUser)
    {
        return new
        {
            pFirstName = appUser.FirstName,
            pLastName = appUser.LastName,
            pEmailId = appUser.EmailId,
            pLoginPass = appUser.LoginPass,
            pUserRole = appUser.UserRole
        };
    }

    /// <summary>
    /// Builds the parameter set for <c>UpdateBlogUser</c>.
    /// </summary>
    /// <remarks>
    /// The optional profile fields are coalesced because the function's parameters are declared
    /// <c>NOT NULL</c>; binding a null would fail the call rather than clear the column.
    /// </remarks>
    /// <param name="blogUser">The user being updated.</param>
    /// <returns>The anonymous parameter object Dapper binds.</returns>
    private static object BuildUpdateParameters(AppUser blogUser)
    {
        return new
        {
            pUserId = blogUser.UserId,
            pFirstName = blogUser.FirstName,
            pLastName = blogUser.LastName,
            pEmailId = blogUser.EmailId,
            pLoginPass = blogUser.LoginPass,
            pUserRole = blogUser.UserRole,
            pProfileImagePath = blogUser.ProfileImagePath ?? string.Empty,
            pProfileDescription = blogUser.ProfileDescription ?? string.Empty,
            pTwitterUrl = blogUser.TwitterUrl ?? string.Empty,
            pLinkedInUrl = blogUser.LinkedInUrl ?? string.Empty,
            pGitHubUrl = blogUser.GitHubUrl ?? string.Empty,
            pPodDescription = blogUser.PodDescription ?? string.Empty,
            pSpeakDescription = blogUser.SpeakDescription ?? string.Empty
        };
    }

    /// <summary>
    /// Builds the parameter set for the résumé-field update.
    /// </summary>
    /// <param name="userId">The user being updated.</param>
    /// <param name="resumeData">The résumé values to write.</param>
    /// <returns>The anonymous parameter object Dapper binds.</returns>
    private static object BuildResumeParameters(long userId, AppUser resumeData)
    {
        return new
        {
            UserId = userId,
            resumeData.Title,
            resumeData.Tagline,
            resumeData.Location,
            resumeData.PhoneNumber,
            resumeData.CVFilePath,
            resumeData.ResumeEnabled,
            resumeData.InstagramUrl
        };
    }
}
