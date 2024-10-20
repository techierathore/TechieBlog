
namespace BlogModels.Models;

public class BlogUser
{
	/// <summary>
	/// Gets or sets the UserID value.
	/// </summary>
	public long UserId	{ get; set; }
	/// <summary>
	/// Gets or sets the FirstName value.
	/// </summary>
	public string FirstName	{ get; set; }
	/// <summary>
	/// Gets or sets the LastName value.
	/// </summary>
	public string LastName	{ get; set; }
	public string FullName
	{
		get
		{
			return FirstName + " " + LastName;
		}
	}

	public string EmailId	{ get; set; }
	/// <summary>
	/// Gets or sets the LoginPass value.
	/// </summary>
	public string LoginPass { get; set; }
	/// <summary>
	/// Gets or sets the CreatedTime value.
	/// </summary>
	public DateTime CreatedOn	{ get; set; }

	/// <summary>
	/// Gets or sets the UpdatedTime value.
	/// </summary>
	public DateTime UpdatedOn	{ get; set; }
    public int UserRoleId { get; set; }
    public bool IsConfirmed { get; set; }
    public string ProfileImagePath	{ get; set; }

	public string ProfileDescription { get; set; }
	public string TwiiterUrl { get; set; }
	public string LinkedInUrl { get; set; }
	public string GitHubUrl { get; set; }
	public string PodDescription { get; set; }
	public string SpeakDescription { get; set; }
	public string AccessToken { get; set; }
	public string RefreshToken { get; set; }
}
