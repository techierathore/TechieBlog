using BlogSvc.DaCore;

namespace BlogModels;
public interface IBlogUserRepo : IGenericRepository<BlogUser>
{
    BlogUser GetLoginUser(string aLoginEmail, string aPassword);
    BlogUser GetUserByEmail(string aLoginEmail);
    BlogUser GetUserByMobile(string aMobileNo);
}
public interface ISvcTokenRepo : IGenericRepository<SvcToken>
{ SvcToken GetSvcToken(long aAppUserId, string aLoginToken); }
public interface IUserLoginRepository : IGenericRepository<UserLogin>
{
    UserLogin GetUserByToken(long aUserId, string aToken);
}
public interface ILoginLogRepo : IGenericRepository<LoginLog>
{ }
public interface IBlogImageRepo : IGenericRepository<BlogImage>
{ }
public interface IBlogPostRepo : IGenericRepository<BlogPost>
{ BlogPost GetTheCounts(); }
public interface IBlogTagRepo : IGenericRepository<BlogTag>
{ }
public interface IBlogCommentRepo : IGenericRepository<BlogComment>
{
    void ApproveBlogComment(long BlogCommentID);
    IEnumerable<BlogComment> GetPagedUnAppComments(int PageSize, int OffSet);
    IEnumerable<BlogComment> GetPostParentComments(long BlogPostID);
    IEnumerable<BlogComment> GetPostChildComments(long BlogPostID);
    AdminCounts GetAdminCounts();
}
public interface IUserEventRepo : IGenericRepository<UserEvent>
{ }
