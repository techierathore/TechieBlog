namespace BlogModels.Models;

public class UserSkill
{
    public long SkillId { get; set; }
    public long UserId { get; set; }
    public string Category { get; set; }
    public string SkillName { get; set; }
    public string? IconPath { get; set; }
    public int DisplayOrder { get; set; }
    public DateTime CreatedOn { get; set; }
}
