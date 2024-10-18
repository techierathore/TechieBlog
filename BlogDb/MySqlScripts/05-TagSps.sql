
CREATE PROCEDURE `GetAllTags`()
BEGIN
   SELECT `TagID`,`TagName` FROM Tag;
END;

CREATE PROCEDURE `TagSelect`(pTagID bigint)
BEGIN
SELECT `TagID`,	`TagName`
FROM Tag WHERE `TagID` = pTagID;
END

CREATE PROCEDURE `TagInsert`(pTagName nvarchar(150))
BEGIN
INSERT INTO Tag (`TagName`) VALUES (pTagName);
END

CREATE PROCEDURE `TagUpdate`(pTagID bigint,	pTagName nvarchar(150))
BEGIN
UPDATE Tag SET `TagName` = pTagName WHERE `TagID` = pTagID;
END