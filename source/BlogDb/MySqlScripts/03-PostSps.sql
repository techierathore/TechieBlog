CREATE PROCEDURE `SelectAllPosts`()
BEGIN

SELECT `PostID`,`Title`,`Abstract`,`PostContent`,`CreatedOn`,`UpdatedOn`,`Published`,
	`Post`.`UserID`,`Tags`,`FeaturedImage` , `BlogUser`.`FirstName` as `BlogWriter`
FROM TechieBlog.Post inner join TechieBlog.BlogUser where `Post`.`UserID` = `BlogUser`.`UserID`;
END;

CREATE PROCEDURE `PostSelect`(BlogPostID bigint)
BEGIN

SELECT `PostID`,`Title`,`Abstract`,`PostContent`,`CreatedOn`,`UpdatedOn`,
	`UserID`,`Tags`,`FeaturedImage`,`Published`
FROM Post WHERE `PostID` = BlogPostID;
END;

CREATE PROCEDURE `PostsByUserID`(BlogUserID bigint)
BEGIN
SELECT `PostID`,`Title`,`Abstract`,`PostContent`,`CreatedOn`,`UpdatedOn`,
	`UserID`,`Tags`,`FeaturedImage`,`Published`
FROM Post WHERE `UserID` = BlogUserID;
END;

CREATE PROCEDURE `GetPagedBlogList`(aPageSize int, aOffset int)
BEGIN
SELECT `PostID`,`Title`,`Abstract`,`PostContent`,
		(Select count(*) From TechieBlog.BlogComment WHERE TechieBlog.BlogComment.PostID = OutErr.PostID) as `CommentCount`,
		`CreatedOn`,`UpdatedOn`,`Published`,`UserID`,`Tags`,`FeaturedImage`           
FROM TechieBlog.Post OutErr Where OutErr.Published =1
Order By OutErr.PostID DESC LIMIT aPageSize OFFSET aOffset ;
END;

CREATE PROCEDURE `PostInsert`(
	IN `Title` VARCHAR(550),
    IN `Abstract` VARCHAR(550), 
    IN `PostContent` LONGTEXT, 
    IN `UserID` BIGINT, 
    IN `Tags` VARCHAR(550), 
    IN `FeaturedImage` VARCHAR(550), `CreatedOn` datetime, 
    IN `Published` BOOLEAN)
BEGIN
INSERT INTO Post (`Title`,`Abstract`,`PostContent`,`UserID`,`Tags`,`FeaturedImage`,`CreatedOn`,`Published`)
VALUES (Title,Abstract, PostContent,UserID,Tags,FeaturedImage,CreatedOn,Published);
END;

CREATE PROCEDURE `PostUpdate`(
    BlogPostID bigint, Title VARCHAR(550), Abstract VARCHAR(550),
    PostContent LONGTEXT, UserID bigint, Tags VARCHAR(550),
    FeaturedImage VARCHAR(550),UpdatedOn datetime, Published BOOLEAN
)
BEGIN
UPDATE Post
SET `Title` = Title, `Abstract` = Abstract, `PostContent` = PostContent,
	`UserID` = UserID, `Tags` = Tags, `FeaturedImage` = FeaturedImage,
    `UpdatedOn` = UpdatedOn, `Published` = Published
WHERE `PostID` = BlogPostID;
END;