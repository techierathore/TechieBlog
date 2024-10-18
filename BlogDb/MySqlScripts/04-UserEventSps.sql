CREATE PROCEDURE `GetUserEvents`(BlogUserID bigint)
BEGIN
SELECT `EventID`,`LogoIconPath`,`EventTitle`,`SessionTitle`,
		`EventUrl`,`EventDate`,`Type`,`UserID`
FROM TechieBlog.UserEvents Where UserID = BlogUserID ORDER BY EventDate DESC;
END

CREATE PROCEDURE `UserEventInsert` (
pLogoIconPath varchar(350), pEventTitle varchar(350), pSessionTitle varchar(350),
pEventUrl varchar(350), pEventDate datetime,pType varchar(50), BlogUserID bigint)
BEGIN
INSERT INTO UserEvents
(`LogoIconPath`,`EventTitle`,`SessionTitle`,`EventUrl`,`EventDate`,`Type`,`UserID`)
VALUES
(pLogoIconPath,pEventTitle,pSessionTitle,pEventUrl,pEventDate,pType,BlogUserID);
END

CREATE PROCEDURE `UserEventSelect` (UserEventID bigint)
BEGIN
SELECT `EventID`,`LogoIconPath`,`EventTitle`,`SessionTitle`,
		`EventUrl`,`EventDate`,`Type`,`UserID`
FROM TechieBlog.UserEvents Where EventID = UserEventID;
END

CREATE PROCEDURE `UserEventUpdate`(
UserEventID bigint, pLogoIconPath varchar(350), pEventTitle varchar(350), pSessionTitle varchar(350),
pEventUrl varchar(350), pEventDate datetime,pType varchar(50), pUserID bigint)
BEGIN
UPDATE UserEvents
SET `LogoIconPath` = pLogoIconPath,
    `EventTitle` = pEventTitle,
	`SessionTitle` = pSessionTitle,
	`EventUrl` = pEventUrl,
	`EventDate` = pEventDate,
	`Type` = pType,
	`UserID` = pUserID
WHERE `EventID` = UserEventID;
END