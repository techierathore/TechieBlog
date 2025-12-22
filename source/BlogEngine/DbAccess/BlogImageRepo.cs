namespace BlogEngine.DbAccess;

/// <summary>
/// Repository for managing BlogImage data access operations using Dapper ORM.
/// </summary>
public class BlogImageRepo : GenericRepository<BlogImage>, IBlogImageRepo
{
    public BlogImageRepo(string connectionString) : base(connectionString) { }

    public override IEnumerable<BlogImage> GetAll()
    {
        using var vConn = GetOpenConnection();
        return vConn.Query<BlogImage>("SELECT * FROM blogimage ORDER BY createdtime DESC");
    }

    public override IEnumerable<BlogImage> GetPagedData(int PageSize, int OffSet)
    {
        using var vConn = GetOpenConnection();
        return vConn.Query<BlogImage>(
            @"SELECT * FROM blogimage ORDER BY createdtime DESC LIMIT @PageSize OFFSET @OffSet",
            new { PageSize, OffSet }).ToList();
    }

    public override IEnumerable<BlogImage> GetAllById(long aSingleId)
    {
        using var vConn = GetOpenConnection();
        return vConn.Query<BlogImage>(
            "SELECT * FROM blogimage WHERE userid = @UserId ORDER BY createdtime DESC",
            new { UserId = aSingleId });
    }

    public override BlogImage GetIntSingle(int aSingleId)
    {
        throw new NotImplementedException();
    }

    public override BlogImage GetSingle(long aSingleId)
    {
        using var vConn = GetOpenConnection();
        return vConn.QueryFirstOrDefault<BlogImage>(
            "SELECT * FROM blogimage WHERE blogimageid = @ImageId",
            new { ImageId = aSingleId });
    }

    /// <summary>
    /// Saves a record to the BlogImages table.
    /// </summary>
    public override void Insert(BlogImage aEntity)
    {
        using var vConn = GetOpenConnection();
        vConn.Execute(
            @"INSERT INTO blogimage (imagename, imagepath, size, createdtime, userid)
              VALUES (@ImageName, @ImagePath, @Size, @CreatedTime, @UserID)",
            new
            {
                aEntity.ImageName,
                aEntity.ImagePath,
                aEntity.Size,
                aEntity.CreatedTime,
                aEntity.UserID
            });
    }

    public override long InsertToGetId(BlogImage entity)
    {
        throw new NotImplementedException();
    }

    public override void Update(BlogImage aEntity)
    {
        using var vConn = GetOpenConnection();
        vConn.Execute(
            @"UPDATE blogimage
              SET imagepath = @ImagePath, size = @Size, createdtime = @CreatedTime, userid = @UserID
              WHERE blogimageid = @BlogImageID",
            new
            {
                aEntity.BlogImageID,
                aEntity.ImagePath,
                aEntity.Size,
                aEntity.CreatedTime,
                aEntity.UserID
            });
    }
}
