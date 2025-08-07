namespace BlogModels;

public class SvcToken
{
	public long SvcTokenId { get; set; }
	public long AppUserId { get; set; }
	public long AppId { get; set; }
	public string LoginToken { get; set; }
	public string TokenStatus { get; set; }
	public DateTime ExipryDate { get; set; }
	public DateTime IssueDate { get; set; }
}


