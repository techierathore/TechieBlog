namespace BlogEngine.DbAccess;

public class BlogTagRepo : GenericRepository<BlogTag>, IBlogTagRepo
{
    public BlogTagRepo(string connectionString) : base(connectionString) { }

    public override IEnumerable<BlogTag> GetAll()
    {
        using var vConn = GetOpenConnection();
        return vConn.Query<BlogTag>("GetAllTags", commandType: CommandType.StoredProcedure).ToList();
    }

    public override IEnumerable<BlogTag> GetAllById(long aSingleId)
    {
        throw new NotImplementedException();
    }

    public override BlogTag GetIntSingle(int aSingleId)
    {
        throw new NotImplementedException();
    }

    public override IEnumerable<BlogTag> GetPagedData(int PageSize, int OffSet)
    {
        throw new NotImplementedException();
    }

    public override BlogTag GetSingle(long aTagId)
    {
        using var vConn = GetOpenConnection();
        var vParams = new DynamicParameters();
        vParams.Add("@pTagID", aTagId);
        return vConn.Query<BlogTag>("TagSelect", vParams, commandType: CommandType.StoredProcedure).FirstOrDefault();
    }

    public override void Insert(BlogTag aTag)
    {
        using var vConn = GetOpenConnection();
        var vParams = new DynamicParameters();
        vParams.Add("@pTagName", aTag.TagName);
        int iResult = vConn.Execute("TagInsert", vParams, commandType: CommandType.StoredProcedure);
    }

    public override long InsertToGetId(BlogTag entity)
    {
        throw new NotImplementedException();
    }

    public override void Update(BlogTag aTag)
    {
        using var vConn = GetOpenConnection();
        var vParams = new DynamicParameters();
        vParams.Add("@pTagID", aTag.TagID);
        vParams.Add("@pTagName", aTag.TagName);
        int iResult = vConn.Execute("TagUpdate", vParams, commandType: CommandType.StoredProcedure);
    }
}
