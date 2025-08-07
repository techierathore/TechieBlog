namespace BlogEngine.DbAccess;

public class BlogPostRepo : GenericRepository<BlogPost>, IBlogPostRepo
{
    public BlogPostRepo(string connectionString) : base(connectionString) { }
    public override IEnumerable<BlogPost> GetAll()
    {
        using var vConn = GetOpenConnection();
        return vConn.Query<BlogPost>("SelectAllPosts", commandType: CommandType.StoredProcedure).ToList();
    }

    /// <summary>
    /// Selects all records from the Posts table based on given
    /// user ID. earlier named 'AllPostsByUserID'
    /// </summary>
    public override IEnumerable<BlogPost> GetAllById(long aUserID)
    {
        using var vConn = GetOpenConnection();
        var vParams = new DynamicParameters();
        vParams.Add("@BlogUserID", aUserID);
        return vConn.Query<BlogPost>("PostsByUserID", vParams, commandType: CommandType.StoredProcedure).ToList();
    }

    public override BlogPost GetIntSingle(int aSingleId)
    {
        throw new NotImplementedException();
    }
    /// <summary>
    /// Earlier named 'GetPostsList'
    /// </summary>
    /// <param name="PageSize"></param>
    /// <param name="OffSet"></param>
    /// <returns></returns>
    public override IEnumerable<BlogPost> GetPagedData(int PageSize, int OffSet)
    {
        using var vConn = GetOpenConnection();
        var vParams = new DynamicParameters();
        vParams.Add("@aPageSize", PageSize);
        vParams.Add("@aOffset", OffSet);
        return vConn.Query<BlogPost>("GetPagedBlogList", vParams, commandType: CommandType.StoredProcedure).ToList();
    }
    public BlogPost GetTheCounts()
    {
        using var vConn = GetOpenConnection();
        return vConn.Query<BlogPost>("GetTheCounts", commandType: CommandType.StoredProcedure).FirstOrDefault();
    }
    public override BlogPost GetSingle(long aSingleId)
    {
        using var vConn = GetOpenConnection();
        var vParams = new DynamicParameters();
        vParams.Add("@BlogPostID", aSingleId);
        return vConn.Query<BlogPost>("PostSelect", vParams, commandType: CommandType.StoredProcedure).FirstOrDefault();
    }

    public override void Insert(BlogPost aPost)
    {
        using var vConn = GetOpenConnection();
        var vParams = new DynamicParameters();
        vParams.Add("@Title", aPost.Title);
        vParams.Add("@Abstract", aPost.Abstract);
        vParams.Add("@PostContent", aPost.PostContent);
        vParams.Add("@UserID", aPost.UserID);
        vParams.Add("@Tags", aPost.Tags);
        vParams.Add("@FeaturedImage", aPost.FeaturedImage);
        vParams.Add("@CreatedOn", aPost.CreatedOn);
        vParams.Add("@Published", aPost.Published);
        int iResult = vConn.Execute("PostInsert", vParams, commandType: CommandType.StoredProcedure);
    }

    public override long InsertToGetId(BlogPost entity)
    {
        throw new NotImplementedException();
    }

    public override void Update(BlogPost aPost)
    {
        using var vConn = GetOpenConnection();
        var vParams = new DynamicParameters();
        vParams.Add("@BlogPostID", aPost.PostID);
        vParams.Add("@Title", aPost.Title);
        vParams.Add("@Abstract", aPost.Abstract);
        vParams.Add("@PostContent", aPost.PostContent);
        vParams.Add("@UserID", aPost.UserID);
        vParams.Add("@Tags", aPost.Tags);
        vParams.Add("@FeaturedImage", aPost.FeaturedImage);
        vParams.Add("@UpdatedOn", aPost.UpdatedOn);
        vParams.Add("@Published", aPost.Published);
        int iResult = vConn.Execute("PostUpdate", vParams, commandType: CommandType.StoredProcedure);
    }
}
