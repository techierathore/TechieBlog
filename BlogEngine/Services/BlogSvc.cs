using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BlogEngine.Services;

public class BlogSvc
{
    private readonly IBlogPostRepo PostRepo;

    public BlogSvc(IBlogPostRepo aPostRepo)
    {
        PostRepo = aPostRepo;
    }

    public IEnumerable<BlogPost> GetAllPosts(long aUserId, bool aIsAdmin)
    {
        IEnumerable<BlogPost> vReturnVal;
        if (aIsAdmin)
        {
            vReturnVal = PostRepo.GetAll();
        }
        else vReturnVal = PostRepo.GetAllById(aUserId);
        return vReturnVal;
    }

    public BlogPost GetSinglePost(long aSingleId)
    {
        var vReturnVal = PostRepo.GetSingle(aSingleId);
        return vReturnVal;
    }
    public void SavePost(BlogPost aObject)
    {
        PostRepo.Insert(aObject);
    }
    public void UpdatePost( BlogPost aObject)
    {
        PostRepo.Update(aObject);
    }

}
