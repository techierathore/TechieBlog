namespace BlogModels.Models;

public class UserAward
{
    public long AwardId { get; set; }
    public long UserId { get; set; }
    public string AwardTitle { get; set; }
    public string? AwardDescription { get; set; }
    public string? BadgeImagePath { get; set; }
    public string? AwardUrl { get; set; }
    public string? AwardYear { get; set; }
    public int DisplayOrder { get; set; }
    public DateTime CreatedOn { get; set; }
}
