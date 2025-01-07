USE  `TechieBlog` ;

CREATE TABLE `BlogComment` (
  `CommentID` bigint(20) NOT NULL AUTO_INCREMENT,
  `PostID` bigint(20) NOT NULL,
  `GivenOn` datetime NOT NULL,
  `GivenBy` varchar(350) NOT NULL,
  `Email` varchar(350) NOT NULL,
  `Comment` varchar(850) NOT NULL,
  `Published` tinyint(1) NOT NULL,
  `ParentCommentID` bigint(20) NOT NULL,
  PRIMARY KEY (`CommentID`)
);

CREATE TABLE `BlogImage` (
  `BlogImageID` bigint(20) NOT NULL AUTO_INCREMENT,
  `ImageName` varchar(150) DEFAULT NULL,
  `ImagePath` varchar(550) NOT NULL,
  `Size` int(11) DEFAULT NULL,
  `CreatedTime` datetime NOT NULL,
  `UserID` bigint(20) NOT NULL,
  PRIMARY KEY (`BlogImageID`)
);
CREATE TABLE BlogUser (
    UserId BIGINT AUTO_INCREMENT PRIMARY KEY,
    FirstName VARCHAR(100) NOT NULL,
    LastName VARCHAR(100) NOT NULL,
    EmailId VARCHAR(255) NOT NULL UNIQUE,
    LoginPass VARCHAR(255) NOT NULL,
    CreatedOn DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UpdatedOn DATETIME ON UPDATE CURRENT_TIMESTAMP,
    UserRole VARCHAR(51) NOT NULL,
    IsConfirmed bit(1) DEFAULT b'0',
    ProfileImagePath VARCHAR(255),
    ProfileDescription TEXT,
    TwiiterUrl VARCHAR(255),
    LinkedInUrl VARCHAR(255),
    GitHubUrl VARCHAR(255),
    PodDescription VARCHAR(1050),
    SpeakDescription VARCHAR(1050)
);

CREATE TABLE `Post` (
  `PostID` bigint(20) NOT NULL AUTO_INCREMENT,
  `Title` varchar(550) NOT NULL,
  `Abstract` varchar(550) DEFAULT NULL,
  `PostContent` longtext NOT NULL,
  `CreatedOn` datetime DEFAULT NULL,
  `UpdatedOn` datetime DEFAULT NULL,
  `UserID` bigint(20) NOT NULL,
  `Tags` varchar(550) NOT NULL,
  `FeaturedImage` varchar(550) NOT NULL,
  `Published` tinyint(1) NOT NULL,
  PRIMARY KEY (`PostID`)
);

CREATE TABLE `Tag` (
  `TagID` bigint(20) NOT NULL AUTO_INCREMENT,
  `TagName` varchar(150) NOT NULL,
  PRIMARY KEY (`TagID`)
);

CREATE TABLE `UserEvents` (
  `EventID` bigint(20) NOT NULL AUTO_INCREMENT,
  `LogoIconPath` varchar(350) DEFAULT NULL,
  `EventTitle` varchar(350) DEFAULT NULL,
  `SessionTitle` varchar(350) DEFAULT NULL,
  `EventUrl` varchar(350) DEFAULT NULL,
  `EventDate` datetime DEFAULT NULL,
  `Type` varchar(50) DEFAULT NULL,
  `UserID` bigint(20) DEFAULT NULL,
  PRIMARY KEY (`EventID`)
);

CREATE TABLE `UserSettings` (
  `SettingsID` int(11) NOT NULL AUTO_INCREMENT,
  `HomeImage` varchar(350) DEFAULT NULL,
  `HomeImageText` varchar(250) DEFAULT NULL,
  `NumberOfLastPost` tinyint(4) DEFAULT NULL,
  `NumberOfCategory` tinyint(4) DEFAULT NULL,
  `PostNumberInPage` tinyint(4) DEFAULT NULL,
  `NumberOfTopPost` tinyint(4) DEFAULT NULL,
  `UpdatedTime` datetime DEFAULT NULL,
  `UserID` bigint(20) DEFAULT NULL,
  PRIMARY KEY (`SettingsID`)
);




