namespace BlogSvc.Controllers;

[Route("[controller]")]
[ApiController]
public class TagSvc : ControllerBase
{
    private readonly IBlogTagRepo TagRepo;

    public TagSvc(IBlogTagRepo aTagRepo)
    {
        TagRepo = aTagRepo;
    }
    [HttpGet("GetAllTags")]
    public IActionResult GetAllTags()
    {
        IEnumerable<BlogTag> vReturnVal = TagRepo.GetAll(); ;
        return Ok(vReturnVal);
    }
    [Route("[action]/{aSingleId}")]
    [HttpGet]
    public IActionResult GetSingleTag(long aSingleId)
    {
        var vReturnVal = TagRepo.GetSingle(aSingleId);
        return Ok(vReturnVal);
    }

    [HttpPost("SaveTag")]
    public IActionResult SaveTag([FromBody] BlogTag aObject)
    {
        if (aObject == null)
        { return BadRequest(); }

        if (!ModelState.IsValid)
        { return BadRequest(ModelState); }
        TagRepo.Insert(aObject);
        return Ok(aObject);
    }

    [HttpPut("UpdateTag")]
    public IActionResult UpdateTag([FromBody] BlogTag aObject)
    {
        if (aObject == null)
        { return BadRequest(); }
        TagRepo.Update(aObject);
        return Ok(aObject);
    }
}
