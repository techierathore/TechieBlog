

namespace BlogEngine.DbAccess;

public class BlogCommentRepo : GenericRepository<BlogComment>, IBlogCommentRepo
{
    public BlogCommentRepo(string connectionString) : base(connectionString)
    {
    }

    public override IEnumerable<BlogComment> GetAll()
    {
        throw new NotImplementedException();
    }
    /// <summary>
    /// Orignally named  'GetPostComments'
    /// </summary>
    /// <param name="aSingleId"></param>
    /// <returns></returns>
    public override IEnumerable<BlogComment> GetAllById(long aBlogPostID)
    {
        IEnumerable<BlogComment> vRetObject = GetPostParentComments(aBlogPostID);
        IEnumerable<BlogComment> vChildObject = GetPostChildComments(aBlogPostID);
        if (vRetObject == null) return null;
        List<BlogComment> vRetChildObject = new List<BlogComment>();
        foreach (var vItem in vRetObject)
        {
            var vReplies = (from c in vChildObject
                            where c.ParentCommentID == vItem.CommentID
                            select c).ToList();
            vItem.Replies = vReplies;
        }
        return vRetObject;
    }
    public IEnumerable<BlogComment> GetPostParentComments(long BlogPostID)
    {
        using var vConn = GetOpenConnection();
        var vParams = new DynamicParameters();
        vParams.Add("@BlogPostID", BlogPostID);
        return vConn.Query<BlogComment>("GetPostParentComments", vParams, commandType: CommandType.StoredProcedure).ToList();
    }
    public IEnumerable<BlogComment> GetPostChildComments(long BlogPostID)
    {
        using var vConn = GetOpenConnection();
        var vParams = new DynamicParameters();
        vParams.Add("@BlogPostID", BlogPostID);
        return vConn.Query<BlogComment>("GetPostChildComments", vParams, commandType: CommandType.StoredProcedure).ToList();
    }
    public AdminCounts GetAdminCounts()
    {
        using var vConn = GetOpenConnection();
        return vConn.Query<AdminCounts>("GetAdminCounts", commandType: CommandType.StoredProcedure).FirstOrDefault();
    }
    public override BlogComment GetIntSingle(int aSingleId)
    {
        throw new NotImplementedException();
    }

    public override IEnumerable<BlogComment> GetPagedData(int PageSize, int OffSet)
    {
        using var vConn = GetOpenConnection();
        var vParams = new DynamicParameters();
        vParams.Add("@aPageSize", PageSize);
        vParams.Add("@aOffset", OffSet);
        return vConn.Query<BlogComment>("GetPagedComments", vParams, commandType: CommandType.StoredProcedure).ToList();
    }
    public IEnumerable<BlogComment> GetPagedUnAppComments(int PageSize, int OffSet)
    {
        using var vConn = GetOpenConnection();
        var vParams = new DynamicParameters();
        vParams.Add("@aPageSize", PageSize);
        vParams.Add("@aOffset", OffSet);
        return vConn.Query<BlogComment>("GetPagedUnAppComments", vParams, commandType: CommandType.StoredProcedure).ToList();
    }
    public override BlogComment GetSingle(long aSingleId)
    {
        using var vConn = GetOpenConnection();
        var vParams = new DynamicParameters();
        vParams.Add("@BlogCommentID", aSingleId);
        return vConn.Query<BlogComment>("BlogCommentSelect", vParams, commandType: CommandType.StoredProcedure).FirstOrDefault();
    }

    public override void Insert(BlogComment aComment)
    {
        using var vConn = GetOpenConnection();
        var vParams = new DynamicParameters();
        vParams.Add("@pPostID", aComment.PostID);
        vParams.Add("@pGivenOn", aComment.GivenOn);
        vParams.Add("@pGivenBy", aComment.GivenBy);
        vParams.Add("@pEmail", aComment.Email);
        vParams.Add("@pComment", aComment.Comment);
        vParams.Add("@pPublish", aComment.Published);
        vParams.Add("@pParentID", aComment.ParentCommentID);
        int iResult = vConn.Execute("BlogCommentInsert", vParams, commandType: CommandType.StoredProcedure);
    }

    public override long InsertToGetId(BlogComment entity)
    {
        throw new NotImplementedException();
    }

    public override void Update(BlogComment aEntityToUpdate)
    {
        throw new NotImplementedException();
    }
    public void ApproveBlogComment(long BlogCommentID)
    {
        using IDbConnection vConn = GetOpenConnection();
        var vParams = new DynamicParameters();
        vParams.Add("@BlogCommentID", BlogCommentID);
        int iResult = vConn.Execute("ApproveBlogComment", vParams, commandType: CommandType.StoredProcedure);
    }
}
