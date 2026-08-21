namespace BlogEngine.DbAccess;

/// <summary>
/// Repository for managing BlogImage data access operations using Dapper ORM.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Persists the metadata describing every uploaded file in the media library.
/// The bytes themselves live in the configured file storage; this table holds the name, the public
/// URL, the size, the owner and the upload time.</para>
///
/// <para><b>Code Flow:</b> <c>BlogImageService</c> injects this repository, calls an <c>…Async</c>
/// member, and the member routes through the protected helpers on <c>GenericRepository</c>, which
/// open the connection asynchronously and flow the cancellation token into the Dapper command.</para>
///
/// <para><b>Dependencies:</b> GenericRepository, Dapper, PostgreSQL, the <c>blogimage</c> table.</para>
///
/// <para><b>Usage:</b> Call the <c>…Async</c> members. The synchronous twins are retained only until
/// the last caller migrates (REQ-NFR-026) and are deleted in the final stage.</para>
///
/// <para><b>Column list, not <c>SELECT *</c>.</b> The reads below name their columns. The model has
/// grown properties — <c>Category</c>, <c>AltText</c>, <c>MimeType</c>, <c>Width</c>,
/// <c>Height</c> — that <c>SELECT *</c> would map only by accident of column order and casing, and
/// the media library filters on <c>Category</c>, so a silently unmapped column there would show an
/// empty library rather than fail.</para>
/// </remarks>
public class BlogImageRepo : GenericRepository<BlogImage>, IBlogImageRepo
{
    private const string SelectColumns = @"
            SELECT blogimageid AS BlogImageID, imagename AS ImageName, imagepath AS ImagePath,
                   size AS Size, createdtime AS CreatedTime, userid AS UserID,
                   COALESCE(category, 'general') AS Category, alttext AS AltText,
                   mimetype AS MimeType, width AS Width, height AS Height
            FROM blogimage";

    private const string SelectAllSql = SelectColumns + " ORDER BY createdtime DESC";

    private const string SelectByIdSql = SelectColumns + " WHERE blogimageid = @ImageId";

    private const string SelectByUserSql =
        SelectColumns + " WHERE userid = @UserId ORDER BY createdtime DESC";

    private const string SelectPagedSql =
        SelectColumns + " ORDER BY createdtime DESC LIMIT @PageSize OFFSET @OffSet";

    private const string InsertSql = @"
            INSERT INTO blogimage (imagename, imagepath, size, createdtime, userid, category, mimetype,
                                   alttext, width, height)
            VALUES (@ImageName, @ImagePath, @Size, @CreatedTime, @UserID, @Category, @MimeType,
                    @AltText, @Width, @Height)";

    private const string InsertReturningIdSql = InsertSql + " RETURNING blogimageid";

    private const string UpdateSql = @"
            UPDATE blogimage
            SET imagepath = @ImagePath, size = @Size, createdtime = @CreatedTime, userid = @UserID,
                category = @Category, mimetype = @MimeType, alttext = @AltText,
                width = @Width, height = @Height
            WHERE blogimageid = @BlogImageID";

    private const string DeleteSql = "DELETE FROM blogimage WHERE blogimageid = @ImageId";

    /// <summary>
    /// Initialises the repository with the application connection string.
    /// </summary>
    /// <param name="connectionString">PostgreSQL connection string from <c>AppDbConString</c>.</param>
    public BlogImageRepo(string connectionString) : base(connectionString) { }

    // =================================================================================================
    // Async surface — the members every caller should use.
    // =================================================================================================

    /// <summary>
    /// Gets every image, newest upload first, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Newest-first is the order the media library presents, so it is
    /// applied in SQL rather than re-sorted per caller.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → buffered query → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>All images, or an empty sequence when none exist.</returns>
    public override async Task<IEnumerable<BlogImage>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await QueryAsync<BlogImage>(SelectAllSql, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets every image belonging to one user, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The uploader is this entity's parent key, so the generic
    /// "all by id" lookup filters on <c>userid</c>.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → filtered query → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="singleId">The uploading user's identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The user's images, newest first, or an empty sequence when there are none.</returns>
    public override async Task<IEnumerable<BlogImage>> GetAllByIdAsync(long singleId, CancellationToken cancellationToken = default)
    {
        return await QueryAsync<BlogImage>(
            SelectByUserSql, new { UserId = singleId }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets a page of images, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Paging is applied in SQL so a large library never crosses the
    /// wire in full.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → LIMIT/OFFSET query → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offSet">Rows to skip.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The requested page, or an empty sequence when the offset is past the end.</returns>
    public override async Task<IEnumerable<BlogImage>> GetPagedDataAsync(int pageSize, int offSet, CancellationToken cancellationToken = default)
    {
        return await QueryAsync<BlogImage>(
            SelectPagedSql, new { PageSize = pageSize, OffSet = offSet }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets one image by its identifier, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> An unknown identifier is a normal answer — a record can be
    /// deleted between listing and opening it — and yields <c>null</c>.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → query by key → first row or <c>null</c>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="singleId">The image identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The image, or <c>null</c> when no row carries that key.</returns>
    public override async Task<BlogImage?> GetSingleAsync(long singleId, CancellationToken cancellationToken = default)
    {
        return await QueryFirstOrDefaultAsync<BlogImage>(
            SelectByIdSql, new { ImageId = singleId }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets one image by INT identifier, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Widens the key and reuses the BIGINT lookup; the column is
    /// <c>BIGINT</c>, so there is no second query to write.</para>
    /// <para><b>Flow:</b> widen → delegate to <see cref="GetSingleAsync"/>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="singleId">The image identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The image, or <c>null</c> when no row carries that key.</returns>
    public override Task<BlogImage?> GetIntSingleAsync(int singleId, CancellationToken cancellationToken = default)
    {
        return GetSingleAsync(singleId, cancellationToken);
    }

    /// <summary>
    /// Records an uploaded file, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The caller does not need the generated key here, so the plain
    /// INSERT is used rather than the RETURNING form.</para>
    /// <para><b>Flow:</b> normalise the timestamp → helper opens the connection asynchronously →
    /// execute INSERT.</para>
    /// <para><b>Side Effects:</b> Adds one row to <c>blogimage</c>.</para>
    /// </remarks>
    /// <param name="entity">The image metadata to persist.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    public override async Task InsertAsync(BlogImage entity, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(InsertSql, BuildWriteParameters(entity), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Records an uploaded file and returns its generated identifier, without blocking.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The upload path needs the key back so it can hand a complete
    /// record to the caller. Shares <see cref="InsertSql"/> with <see cref="InsertAsync"/>, so the
    /// two write paths can never insert different columns.</para>
    /// <para><b>Flow:</b> normalise the timestamp → helper opens the connection asynchronously →
    /// INSERT … RETURNING → read scalar.</para>
    /// <para><b>Side Effects:</b> Adds one row to <c>blogimage</c>.</para>
    /// </remarks>
    /// <param name="entity">The image metadata to persist.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>The generated <c>blogimageid</c>.</returns>
    public override async Task<long> InsertToGetIdAsync(BlogImage entity, CancellationToken cancellationToken = default)
    {
        return await QuerySingleAsync<long>(
            InsertReturningIdSql, BuildWriteParameters(entity), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Updates an image's metadata, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The original file name is never rewritten — it is what the user
    /// recognises the upload by — so it is absent from the UPDATE.</para>
    /// <para><b>Flow:</b> normalise the timestamp → helper opens the connection asynchronously →
    /// execute UPDATE.</para>
    /// <para><b>Side Effects:</b> Updates one row, matched on <c>blogimageid</c>.</para>
    /// </remarks>
    /// <param name="entityToUpdate">The image metadata carrying the new values.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    public override async Task UpdateAsync(BlogImage entityToUpdate, CancellationToken cancellationToken = default)
    {
        var parameters = BuildWriteParameters(entityToUpdate);
        parameters.Add("BlogImageID", entityToUpdate.BlogImageID);
        await ExecuteAsync(UpdateSql, parameters, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes an image's metadata row, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> <c>DELETE FROM blogimage WHERE blogimageid = @ImageId</c> — keyed
    /// and parameterised, so it can never clear the table. It returns whether a row went, which lets
    /// the caller tell "deleted" from "already gone" and decide whether the file on disk still needs
    /// removing.</para>
    /// <para><b>Flow:</b> bind the key → helper opens the connection asynchronously → execute DELETE →
    /// compare the affected count to zero.</para>
    /// <para><b>Side Effects:</b> Removes the database row only. The stored file itself is <b>not</b>
    /// touched here — deleting the bytes is the storage layer's job, and a caller that forgets it
    /// leaves an orphaned file behind.</para>
    /// </remarks>
    /// <param name="imageId">The image metadata row to delete.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    /// <returns><c>true</c> when a row was deleted; <c>false</c> when the id was unknown.</returns>
    public async Task<bool> DeleteAsync(long imageId, CancellationToken cancellationToken = default)
    {
        var affected = await ExecuteAsync(
            DeleteSql, new { ImageId = imageId }, cancellationToken).ConfigureAwait(false);
        return affected > 0;
    }

    /// <summary>
    /// Binds the columns shared by the insert and update statements.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> <c>createdtime</c> is a <c>TIMESTAMP</c> column and the upload
    /// path supplies <c>DateTime.UtcNow</c>, whose <c>Kind</c> is <c>Utc</c>. Npgsql infers the wire
    /// type from the Kind, so an unnormalised value is sent as <c>timestamptz</c> and PostgreSQL
    /// converts it into the session time zone on the way into the column — which would silently
    /// misorder the media library on any host whose session zone is not UTC.
    /// <c>DbTimestamp.AsTimestamp</c> drops the Kind without moving the instant.</para>
    /// <para><b>Flow:</b> copy the writable fields, normalising the timestamp.</para>
    /// <para><b>Side Effects:</b> None; pure.</para>
    /// <para><b>Width and height are written, not just read (REQ-FN-026).</b> They were absent from
    /// both statements, so <c>blogimage.width</c> and <c>blogimage.height</c> stayed NULL on every
    /// row no matter what the caller set on the model — the columns existed but carried nothing. A
    /// dimension the upload path could not determine is bound as NULL deliberately: "unknown" and
    /// "zero pixels" are different answers and the column must not conflate them.</para>
    /// </remarks>
    /// <param name="entity">The image metadata being written.</param>
    /// <returns>Parameters for the write statement.</returns>
    private static DynamicParameters BuildWriteParameters(BlogImage entity)
    {
        var parameters = new DynamicParameters();
        parameters.Add("ImageName", entity.ImageName);
        parameters.Add("ImagePath", entity.ImagePath);
        parameters.Add("Size", entity.Size);
        parameters.Add("CreatedTime", DbTimestamp.AsTimestamp(entity.CreatedTime));
        parameters.Add("UserID", entity.UserID);
        parameters.Add("Category", string.IsNullOrWhiteSpace(entity.Category) ? "general" : entity.Category);
        parameters.Add("MimeType", entity.MimeType);
        parameters.Add("AltText", entity.AltText);
        parameters.Add("Width", entity.Width);
        parameters.Add("Height", entity.Height);
        return parameters;
    }

    // =================================================================================================
    // Legacy blocking surface — REQ-NFR-026 deletes these once every caller has migrated.
    // Each one executes the same SQL constant as its async twin, so the two cannot drift.
    // =================================================================================================

    /// <summary>
    /// Gets every image, newest upload first.
    /// </summary>
    /// <returns>All images.</returns>
    public override IEnumerable<BlogImage> GetAll()
    {
        using var connection = GetOpenConnection();
        return connection.Query<BlogImage>(SelectAllSql).ToList();
    }

    /// <summary>
    /// Gets a page of images.
    /// </summary>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offSet">Rows to skip.</param>
    /// <returns>The requested page.</returns>
    public override IEnumerable<BlogImage> GetPagedData(int pageSize, int offSet)
    {
        using var connection = GetOpenConnection();
        return connection.Query<BlogImage>(SelectPagedSql, new { PageSize = pageSize, OffSet = offSet }).ToList();
    }

    /// <summary>
    /// Gets every image belonging to one user.
    /// </summary>
    /// <param name="singleId">The uploading user's identifier.</param>
    /// <returns>The user's images, newest first.</returns>
    public override IEnumerable<BlogImage> GetAllById(long singleId)
    {
        using var connection = GetOpenConnection();
        return connection.Query<BlogImage>(SelectByUserSql, new { UserId = singleId }).ToList();
    }

    /// <summary>
    /// Gets one image by INT identifier.
    /// </summary>
    /// <param name="singleId">The image identifier.</param>
    /// <returns>The image, or <c>null</c> when not found.</returns>
    public override BlogImage? GetIntSingle(int singleId)
    {
        return GetSingle(singleId);
    }

    /// <summary>
    /// Gets one image by its identifier.
    /// </summary>
    /// <param name="singleId">The image identifier.</param>
    /// <returns>The image, or <c>null</c> when not found.</returns>
    public override BlogImage? GetSingle(long singleId)
    {
        using var connection = GetOpenConnection();
        return connection.QueryFirstOrDefault<BlogImage>(SelectByIdSql, new { ImageId = singleId });
    }

    /// <summary>
    /// Records an uploaded file.
    /// </summary>
    /// <param name="entity">The image metadata to persist.</param>
    public override void Insert(BlogImage entity)
    {
        using var connection = GetOpenConnection();
        connection.Execute(InsertSql, BuildWriteParameters(entity));
    }

    /// <summary>
    /// Records an uploaded file and returns its generated identifier.
    /// </summary>
    /// <param name="entity">The image metadata to persist.</param>
    /// <returns>The generated <c>blogimageid</c>.</returns>
    public override long InsertToGetId(BlogImage entity)
    {
        using var connection = GetOpenConnection();
        return connection.QuerySingle<long>(InsertReturningIdSql, BuildWriteParameters(entity));
    }

    /// <summary>
    /// Updates an image's metadata.
    /// </summary>
    /// <param name="entityToUpdate">The image metadata carrying the new values.</param>
    public override void Update(BlogImage entityToUpdate)
    {
        var parameters = BuildWriteParameters(entityToUpdate);
        parameters.Add("BlogImageID", entityToUpdate.BlogImageID);
        using var connection = GetOpenConnection();
        connection.Execute(UpdateSql, parameters);
    }
}
