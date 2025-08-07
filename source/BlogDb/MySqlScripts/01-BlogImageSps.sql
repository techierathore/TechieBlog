CREATE PROCEDURE `BlogImageInsert`(
	ImageName nvarchar(150),
	ImagePath nvarchar(550),
	Size int,
	CreatedTime datetime,
	UserID bigint
)
BEGIN
INSERT INTO BlogImage
(`ImageName`,`ImagePath`,`Size`,`CreatedTime`,`UserID`)
VALUES
( ImageName,ImagePath,Size,CreatedTime,UserID );
END;

CREATE PROCEDURE `GetPagedBlogImages`(aPageSize int, aOffset int)
BEGIN
SELECT `BlogImageID`,`ImageName`,`ImagePath`,`Size`,`CreatedTime`,`UserID`
FROM BlogImage Order By `BlogImageID` DESC LIMIT aPageSize OFFSET aOffset ;
END