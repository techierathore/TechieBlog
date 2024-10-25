-- Stored Procedures for BlogUser

CREATE PROCEDURE InsertBlogUser (
    IN FirstName VARCHAR(255),
    IN LastName VARCHAR(255),
    IN EmailId VARCHAR(255),
    IN LoginPass VARCHAR(255),
    IN UserRoleId INT,
    IN ProfileImagePath VARCHAR(255),
    IN ProfileDescription TEXT,
    IN TwiiterUrl VARCHAR(255),
    IN LinkedInUrl VARCHAR(255),
    IN GitHubUrl VARCHAR(255),
    IN PodDescription TEXT,
    IN SpeakDescription TEXT,
    IN AccessToken TEXT,
    IN RefreshToken TEXT
)
BEGIN
    INSERT INTO BlogUser 
    (FirstName, LastName, EmailId, LoginPass, CreatedOn, UpdatedOn, UserRoleId, ProfileImagePath, ProfileDescription, TwiiterUrl, LinkedInUrl, GitHubUrl, PodDescription, SpeakDescription, AccessToken, RefreshToken)
    VALUES 
    (FirstName, LastName, EmailId, LoginPass, NOW(), NOW(), UserRoleId, ProfileImagePath, ProfileDescription, TwiiterUrl, LinkedInUrl, GitHubUrl, PodDescription, SpeakDescription, AccessToken, RefreshToken);
END;

CREATE PROCEDURE UpdateBlogUser (
    IN UserId BIGINT,
    IN FirstName VARCHAR(255),
    IN LastName VARCHAR(255),
    IN EmailId VARCHAR(255),
    IN LoginPass VARCHAR(255),
    IN UserRoleId INT,
    IN ProfileImagePath VARCHAR(255),
    IN ProfileDescription TEXT,
    IN TwiiterUrl VARCHAR(255),
    IN LinkedInUrl VARCHAR(255),
    IN GitHubUrl VARCHAR(255),
    IN PodDescription TEXT,
    IN SpeakDescription TEXT,
    IN AccessToken TEXT,
    IN RefreshToken TEXT
)
BEGIN
    UPDATE BlogUser 
    SET 
        FirstName = FirstName,
        LastName = LastName,
        EmailId = EmailId,
        LoginPass = LoginPass,
        UpdatedOn = NOW(),
        UserRoleId = UserRoleId,
        ProfileImagePath = ProfileImagePath,
        ProfileDescription = ProfileDescription,
        TwiiterUrl = TwiiterUrl,
        LinkedInUrl = LinkedInUrl,
        GitHubUrl = GitHubUrl,
        PodDescription = PodDescription,
        SpeakDescription = SpeakDescription,
        AccessToken = AccessToken,
        RefreshToken = RefreshToken
    WHERE UserId = UserId;
END;

CREATE PROCEDURE SelectBlogUserById (
    IN UserId BIGINT
)
BEGIN
    SELECT * FROM BlogUser WHERE UserId = UserId;
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
