

namespace BlogSvc.Controllers;

[Route("[controller]")]
[ApiController]
public class BlogSvc : ControllerBase
{
    private readonly IBlogPostRepo PostRepo;

    public BlogSvc(IBlogPostRepo aPostRepo)
    {
        PostRepo = aPostRepo;
    }
    [Route("[action]/{aUserId}/{aIsAdmin}")]
    [HttpGet]
    public IActionResult GetAllPosts(long aUserId, bool aIsAdmin)
    {
        IEnumerable<BlogPost> vReturnVal;
        if (aIsAdmin)
        {
            vReturnVal = PostRepo.GetAll();
        }
        else vReturnVal = PostRepo.GetAllById(aUserId);
        return Ok(vReturnVal);
    }
    [Route("[action]/{aSingleId}")]
    [HttpGet]
    public IActionResult GetSinglePost(long aSingleId)
    {
        var vReturnVal = PostRepo.GetSingle(aSingleId);
        return Ok(vReturnVal);
    }

    [HttpPost("SavePost")]
    public IActionResult SavePost([FromBody] BlogPost aObject)
    {
        if (aObject == null)
        { return BadRequest(); }

        if (!ModelState.IsValid)
        { return BadRequest(ModelState); }
        PostRepo.Insert(aObject);
        return Ok(aObject);
    }

    [HttpPut("UpdatePost")]
    public IActionResult UpdatePost([FromBody] BlogPost aObject)
    {
        if (aObject == null)
        { return BadRequest(); }
        PostRepo.Update(aObject);
        return Ok(aObject);
    }

}
