namespace BlogEngine.DbAccess;

/// <summary>
/// Repository for managing BlogComment data access operations using Dapper ORM.
/// </summary>
public class BlogCommentRepo : GenericRepository<BlogComment>, IBlogCommentRepo
{
    public BlogCommentRepo(string connectionString) : base(connectionString)
    {
    }

    public override IEnumerable<BlogComment> GetAll()
    {
        using var vConn = GetOpenConnection();
        return vConn.Query<BlogComment>("SELECT * FROM blogcomment ORDER BY commentid DESC");
    }

    /// <summary>
    /// Gets all comments for a blog post including replies
    /// </summary>
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
        return vConn.Query<BlogComment>(
            @"SELECT * FROM blogcomment
              WHERE postid = @BlogPostID AND parentcommentid IS NULL AND published = true
              ORDER BY givenon DESC",
            new { BlogPostID }).ToList();
    }

    public IEnumerable<BlogComment> GetPostChildComments(long BlogPostID)
    {
        using var vConn = GetOpenConnection();
        return vConn.Query<BlogComment>(
            @"SELECT * FROM blogcomment
              WHERE postid = @BlogPostID AND parentcommentid IS NOT NULL AND published = true
              ORDER BY givenon ASC",
            new { BlogPostID }).ToList();
    }

    public AdminCounts GetAdminCounts()
    {
        using var vConn = GetOpenConnection();
        return vConn.QueryFirstOrDefault<AdminCounts>(
            @"SELECT
                (SELECT COUNT(*) FROM BlogPost WHERE IsDeleted = FALSE OR IsDeleted IS NULL) AS BlogCount,
                (SELECT COUNT(*) FROM blogcomment) AS CommentCount,
                (SELECT COUNT(*) FROM blogcomment WHERE published = false) AS UnAppComments,
                (SELECT COUNT(*) FROM bloguser) AS UserCount");
    }

    public override BlogComment GetIntSingle(int aSingleId)
    {
        using var vConn = GetOpenConnection();
        return vConn.QueryFirstOrDefault<BlogComment>(
            "SELECT * FROM blogcomment WHERE commentid = @CommentId",
            new { CommentId = aSingleId });
    }

    public override IEnumerable<BlogComment> GetPagedData(int PageSize, int OffSet)
    {
        using var vConn = GetOpenConnection();
        return vConn.Query<BlogComment>(
            @"SELECT * FROM blogcomment ORDER BY givenon DESC LIMIT @PageSize OFFSET @OffSet",
            new { PageSize, OffSet }).ToList();
    }

    public IEnumerable<BlogComment> GetPagedUnAppComments(int PageSize, int OffSet)
    {
        using var vConn = GetOpenConnection();
        return vConn.Query<BlogComment>(
            @"SELECT * FROM blogcomment WHERE published = false ORDER BY givenon DESC LIMIT @PageSize OFFSET @OffSet",
            new { PageSize, OffSet }).ToList();
    }

    public override BlogComment GetSingle(long aSingleId)
    {
        using var vConn = GetOpenConnection();
        return vConn.QueryFirstOrDefault<BlogComment>(
            "SELECT * FROM blogcomment WHERE commentid = @CommentId",
            new { CommentId = aSingleId });
    }

    public override void Insert(BlogComment aComment)
    {
        using var vConn = GetOpenConnection();
        vConn.Execute(
            @"INSERT INTO blogcomment (postid, givenon, givenby, email, comment, published, parentcommentid)
              VALUES (@PostID, @GivenOn, @GivenBy, @Email, @Comment, @Published, @ParentCommentID)",
            new
            {
                aComment.PostID,
                aComment.GivenOn,
                aComment.GivenBy,
                aComment.Email,
                aComment.Comment,
                aComment.Published,
                aComment.ParentCommentID
            });
    }

    public override long InsertToGetId(BlogComment entity)
    {
        using var vConn = GetOpenConnection();
        return vConn.QuerySingle<long>(
            @"INSERT INTO blogcomment (postid, givenon, givenby, email, comment, published, parentcommentid)
              VALUES (@PostID, @GivenOn, @GivenBy, @Email, @Comment, @Published, @ParentCommentID)
              RETURNING commentid",
            new
            {
                entity.PostID,
                entity.GivenOn,
                entity.GivenBy,
                entity.Email,
                entity.Comment,
                entity.Published,
                entity.ParentCommentID
            });
    }

    public override void Update(BlogComment aEntityToUpdate)
    {
        using var vConn = GetOpenConnection();
        vConn.Execute(
            @"UPDATE blogcomment 
              SET postid = @PostID, 
                  givenby = @GivenBy, 
                  email = @Email, 
                  comment = @Comment, 
                  published = @Published, 
                  parentcommentid = @ParentCommentID
              WHERE commentid = @CommentID",
            new
            {
                aEntityToUpdate.PostID,
                aEntityToUpdate.GivenBy,
                aEntityToUpdate.Email,
                aEntityToUpdate.Comment,
                aEntityToUpdate.Published,
                aEntityToUpdate.ParentCommentID,
                aEntityToUpdate.CommentID
            });
    }

    public void ApproveBlogComment(long BlogCommentID)
    {
        using IDbConnection vConn = GetOpenConnection();
        vConn.Execute(
            "UPDATE blogcomment SET published = true WHERE commentid = @CommentId",
            new { CommentId = BlogCommentID });
    }

    public void Delete(long commentId)
    {
        using var vConn = GetOpenConnection();
        vConn.Execute(
            "DELETE FROM blogcomment WHERE commentid = @CommentId",
            new { CommentId = commentId });
    }

    public IEnumerable<BlogComment> GetPendingComments()
    {
        using var vConn = GetOpenConnection();
        return vConn.Query<BlogComment>(
            "SELECT * FROM blogcomment WHERE published = false ORDER BY givenon DESC").ToList();
    }

    public int GetTotalCount()
    {
        using var vConn = GetOpenConnection();
        return vConn.QuerySingle<int>("SELECT COUNT(*) FROM blogcomment");
    }

    public int GetPendingCount()
    {
        using var vConn = GetOpenConnection();
        return vConn.QuerySingle<int>("SELECT COUNT(*) FROM blogcomment WHERE published = false");
    }
}
