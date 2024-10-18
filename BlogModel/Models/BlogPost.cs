namespace BlogModels
{
    public class BlogPost
	{
		/// <summary>
		/// Gets or sets the PostID value.
		/// </summary>
		public long PostID	{ get; set; }

		/// <summary>
		/// Gets or sets the Title value.
		/// </summary>
		public string Title { get; set; }
		public string UIPageTitle { get; set; }
		public string Abstract { get; set; }
		/// <summary>
		/// Gets or sets the PostContent value.
		/// </summary>
		public string PostContent { get; set; }

		/// <summary>
		/// Gets or sets the CreatedTime value.
		/// </summary>
		public DateTime CreatedOn { get; set; }

		/// <summary>
		/// Gets or sets the UpdatedTime value.
		/// </summary>
		public DateTime UpdatedOn { get; set; }

		/// <summary>
		/// Gets or sets the UserID value.
		/// </summary>
		public long UserID { get; set; }

		/// <summary>
		/// Gets or sets the Tags value.
		/// </summary>
		public string Tags { get; set; }

		/// <summary>
		/// Gets or sets the CategoryId value.
		/// </summary>
		public int CategoryId { get; set; }

		public string BlogWriter { get; set; }

		/// <summary>
		/// Gets or sets the FeaturedImage value.
		/// </summary>
		public string FeaturedImage { get; set; }
		public bool Published { get; set; }

		public long CommentCount { get; set; }
		public int BlogCount { get; set; }
	}
}