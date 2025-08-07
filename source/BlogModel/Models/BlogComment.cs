using System.ComponentModel.DataAnnotations;

namespace BlogModels;
/// <summary>
/// The Class to manage and save Blog Comments
/// </summary>
public class BlogComment
{
    /// <summary>
    /// Gets or sets the CommentID value.
    /// </summary>
    public long CommentID  { get; set; }
    /// <summary>
    /// Gets or sets the PostID value.
    /// </summary>
    public long PostID { get; set; }
    /// <summary>
    /// Gets or sets the ComntDateTime value.
    /// </summary>
    public DateTime GivenOn { get; set; }
    /// <summary>
    /// Gets or sets the Name value.
    /// </summary>
    [Required(ErrorMessage = "Name is required")]
    public string GivenBy { get; set; }
    /// <summary>
    /// Gets or sets the Email value.
    /// </summary>
    [Required(ErrorMessage = "Email is required")]
    [RegularExpression(@"^([a-zA-Z0-9_\.\-])+\@(([a-zA-Z0-9\-])+\.)+([a-zA-Z0-9]{2,4})+$", ErrorMessage = "Please Enter Correct Email Address")]
    public string Email { get; set; }
    /// <summary>
    /// Gets or sets the Comment value.
    /// </summary>
    [Required(ErrorMessage = "Comment is required")]
    public string Comment { get; set; }
    /// <summary>
    /// Gets or sets the Publish value.
    /// </summary>
    public bool Published { get; set; }
    public long ParentCommentID { get; set; }
    public List<BlogComment> Replies { get; set; }
}
