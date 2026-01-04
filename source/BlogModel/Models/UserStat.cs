namespace BlogModels.Models;

public class UserStat
{
    public long StatId { get; set; }
    public long UserId { get; set; }
    public string StatLabel { get; set; }
    public string StatValue { get; set; }
    public string? StatCategory { get; set; }
    public int DisplayOrder { get; set; }
}
