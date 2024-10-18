namespace BlogEngine.DbAccess;


public class BlogImageRepo : GenericRepository<BlogImage>, IBlogImageRepo
{
    public BlogImageRepo(string connectionString) : base(connectionString) { }

    public override IEnumerable<BlogImage> GetAll()
    {
        throw new NotImplementedException();
    }
    public override IEnumerable<BlogImage> GetPagedData(int PageSize, int OffSet)
    {
        using var vConn = GetOpenConnection();
        var vParams = new DynamicParameters();
        vParams.Add("@aPageSize", PageSize);
        vParams.Add("@aOffset", OffSet);
        return vConn.Query<BlogImage>("GetPagedBlogImages", vParams, commandType: CommandType.StoredProcedure).ToList();
    }
    public override IEnumerable<BlogImage> GetAllById(long aSingleId)
    {
        throw new NotImplementedException();
    }

    public override BlogImage GetIntSingle(int aSingleId)
    {
        throw new NotImplementedException();
    }

    public override BlogImage GetSingle(long aSingleId)
    {
        throw new NotImplementedException();
    }
    /// <summary>
    /// Saves a record to the BlogImages table.
    /// returns True if value saved successfullyelse false
    /// Throw exception with message value 'EXISTS' if the data is duplicate
    /// </summary>
    public override void Insert(BlogImage aEntity)
    {
        using var vConn = GetOpenConnection();
        var vParams = new DynamicParameters();
        vParams.Add("@ImageName", aEntity.ImageName);
        vParams.Add("@ImagePath", aEntity.ImagePath);
        vParams.Add("@Size", aEntity.Size);
        vParams.Add("@CreatedTime", aEntity.CreatedTime);
        vParams.Add("@UserID", aEntity.UserID);
        int iResult = vConn.Execute("BlogImageInsert", vParams, commandType: CommandType.StoredProcedure);
    }

    public override long InsertToGetId(BlogImage entity)
    {
        throw new NotImplementedException();
    }

    public override void Update(BlogImage aEntity)
    {
        using var vConn = GetOpenConnection();
        var vParams = new DynamicParameters();
        vParams.Add("@BlogImageID", aEntity.BlogImageID);
        vParams.Add("@ImagePath", aEntity.ImagePath);
        vParams.Add("@Size", aEntity.Size);
        vParams.Add("@CreatedTime", aEntity.CreatedTime);
        vParams.Add("@UserID", aEntity.UserID);
        int iResult = vConn.Execute("BlogImagesUpdate", vParams, commandType: CommandType.StoredProcedure);
    }
}
