CREATE PROCEDURE `BlogCommentSelect`(BlogCommentID bigint)
BEGIN
SELECT `CommentID`,`PostID`,`GivenOn`,`GivenBy`,`Email`,`Comment`,`Published`,`ParentCommentID`
FROM BlogComment Where `CommentID` = BlogCommentID;
END;

CREATE PROCEDURE `ApproveBlogComment`(BlogCommentID bigint)
BEGIN
UPDATE BlogComment SET	`Published` = 1 WHERE `CommentID` = BlogCommentID;
END;

CREATE PROCEDURE `GetPostParentComments`(BlogPostID bigint)
BEGIN
SELECT `CommentID`,`PostID`,`GivenOn`,`GivenBy`,`Email`,`Comment`,`Published`,`ParentCommentID`
FROM BlogComment Where `Published` = 1 AND (`ParentCommentID` is null OR `ParentCommentID` = 0)  AND `PostID` = BlogPostID;
END;

CREATE PROCEDURE `GetPostChildComments`(BlogPostID bigint)
BEGIN
SELECT `CommentID`,`PostID`,`GivenOn`,`GivenBy`,`Email`,`Comment`,`Published`,`ParentCommentID`
FROM BlogComment Where `Published` = 1 AND `ParentCommentID` is not null AND `PostID` = BlogPostID;
END;

CREATE PROCEDURE `GetPagedUnAppComments`(aPageSize int, aOffset int)
BEGIN
SELECT `CommentID`,`PostID`,`GivenOn`,`GivenBy`,`Email`,`Comment`,`Published`
FROM BlogComment Where `Published` = 0  Order By `CommentID` DESC LIMIT aPageSize OFFSET aOffset ;
END;

CREATE PROCEDURE `GetPagedComments`(aPageSize int, aOffset int)
BEGIN
SELECT `CommentID`,`PostID`,`GivenOn`,`GivenBy`,`Email`,`Comment`,`Published`
FROM BlogComment Order By `CommentID` DESC LIMIT aPageSize OFFSET aOffset ;
END;

CREATE PROCEDURE `BlogCommentInsert`(
	pPostID bigint, pGivenOn datetime, pGivenBy varchar(350),
    pEmail varchar(350), pComment varchar(850), pPublish BOOLEAN,
    pParentID bigint
)
BEGIN
INSERT INTO BlogComment
(`PostID`,`GivenOn`,`GivenBy`,`Email`,`Comment`,`Published`,`ParentCommentID`)
VALUES
(pPostID,pGivenOn,pGivenBy,pEmail,pComment,pPublish,pParentID);
END;

