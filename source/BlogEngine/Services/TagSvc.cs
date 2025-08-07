
namespace BlogEngine.Services;

public class TagSvc
{
    private readonly IBlogTagRepo TagRepo;

    public TagSvc(IBlogTagRepo aTagRepo)
    {
        TagRepo = aTagRepo;
    }
    public IEnumerable<BlogTag> GetAllTags()
    {
        IEnumerable<BlogTag> vReturnVal = TagRepo.GetAll(); ;
        return vReturnVal;
    }
    public BlogTag GetSingleTag(long aSingleId)
    {
        var vReturnVal = TagRepo.GetSingle(aSingleId);
        return vReturnVal;
    }

    public void AddTag(BlogTag aObject) {
        TagRepo.Insert(aObject);
    }

    public void UpdateTag(BlogTag aObject) { TagRepo.Update(aObject); }

}
