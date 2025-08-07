
using System;

namespace BlogModels.Models;
/// <summary>
/// the main application user Class
/// </summary>
public class AppUser
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
    public string UserRole { get; set; }
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


public static class SampleUsers
{
    public static AppUser Avatar1 { get; set; } = new()
    {
        FirstName = "Daniel",
        LastName = "Mccoy",
        ProfileImagePath = "img/avatars/avatar.jpg",
    };

    public static AppUser Avatar2 { get; set; } = new()
    {
        FirstName = "Dale",
        LastName = "Summers",
        ProfileImagePath = "img/avatars/avatar-2.jpg",
    };

    public static AppUser Avatar3 { get; set; } = new()
    {
        FirstName = "Mary",
        LastName = "Fletcher",
        ProfileImagePath = "img/avatars/avatar-3.jpg",
    };

    public static AppUser Avatar4 { get; set; } = new()
    {
        FirstName = "Anne",
        LastName = "Cameron",
        ProfileImagePath = "img/avatars/avatar-4.jpg",
    };
}