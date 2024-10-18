CREATE PROCEDURE `BlogUsersInsert`
   (IN `FirstName` VARCHAR(250), 
    IN `LastName` VARCHAR(250), 
    IN `EmailID` VARCHAR(550), 
    IN `LoginPassword` VARCHAR(20), 
    IN `Role` VARCHAR(25), 
    IN `CreatedTime` DATETIME, 
    IN `UpdatedTime` DATETIME)
BEGIN
INSERT INTO BlogUser
(`FirstName`,`LastName`,`EmailID`,`LoginPassword`,`Role`,`CreatedTime`,`UpdatedTime`)
VALUES
(FirstName,LastName,EmailID,LoginPassword,Role,CreatedTime,UpdatedTime);
END;

CREATE PROCEDURE `GetLoginUser`(LoginMail nvarchar(550),
	LoginPassword nvarchar(20))
BEGIN
SELECT `UserID`,`FirstName`,`LastName`,`EmailID`,`LoginPassword`,`Role`,`CreatedTime`,`UpdatedTime`,`LastLogin`
FROM BlogUser WHERE EmailID = LoginMail and LoginPassword = LoginPassword;
END;

CREATE PROCEDURE `GetUserByEmail`(IN `LoginMail` VARCHAR(550)
BEGIN
SELECT `UserID`,`FirstName`,`LastName`,`EmailID`,`LoginPassword`,`Role`,`CreatedTime`,`UpdatedTime`,`LastLogin`
