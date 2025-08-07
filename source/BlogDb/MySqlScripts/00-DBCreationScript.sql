USE `TechieBlog`;

-- Blog Management
CREATE TABLE `BlogComment` (
  `CommentID` bigint(20) NOT NULL AUTO_INCREMENT,
  `PostID` bigint(20) NOT NULL,
  `GivenOn` datetime NOT NULL,
  `GivenBy` varchar(350) NOT NULL,
  `Email` varchar(350) NOT NULL,
  `Comment` varchar(850) NOT NULL,
  `Published` tinyint(1) NOT NULL,
  `ParentCommentID` bigint(20) DEFAULT NULL,
  PRIMARY KEY (`CommentID`),
  FOREIGN KEY (`PostID`) REFERENCES `Post`(`PostID`)
);

CREATE TABLE `BlogImage` (
  `BlogImageID` bigint(20) NOT NULL AUTO_INCREMENT,
  `ImageName` varchar(150) DEFAULT NULL,
  `ImagePath` varchar(550) NOT NULL,
  `Size` int(11) DEFAULT NULL,
  `CreatedTime` datetime NOT NULL,
  `UserID` bigint(20) NOT NULL,
  PRIMARY KEY (`BlogImageID`),
  FOREIGN KEY (`UserID`) REFERENCES `BlogUser`(`UserId`)
);

CREATE TABLE `BlogUser` (
  `UserId` BIGINT AUTO_INCREMENT PRIMARY KEY,
  `FirstName` VARCHAR(100) NOT NULL,
  `LastName` VARCHAR(100) NOT NULL,
  `EmailId` VARCHAR(255) NOT NULL UNIQUE,
  `LoginPass` VARCHAR(255) NOT NULL,
  `CreatedOn` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `UpdatedOn` DATETIME ON UPDATE CURRENT_TIMESTAMP,
  `UserRole` VARCHAR(51) NOT NULL,
  `IsConfirmed` BIT(1) DEFAULT b'0',
  `ProfileImagePath` VARCHAR(255),
  `ProfileDescription` TEXT,
  `TwitterUrl` VARCHAR(255),
  `LinkedInUrl` VARCHAR(255),
  `GitHubUrl` VARCHAR(255),
  `PodDescription` VARCHAR(1050),
  `SpeakDescription` VARCHAR(1050)
);

CREATE TABLE `Category` (
  `CategoryID` bigint(20) NOT NULL AUTO_INCREMENT,
  `CategoryName` varchar(150) NOT NULL,
  PRIMARY KEY (`CategoryID`)
);

CREATE TABLE `Post` (
  `PostID` bigint(20) NOT NULL AUTO_INCREMENT,
  `Title` varchar(550) NOT NULL,
  `Abstract` varchar(550) DEFAULT NULL,
  `PostContent` longtext NOT NULL,
  `CreatedOn` datetime DEFAULT NULL,
  `UpdatedOn` datetime DEFAULT NULL,
  `UserID` bigint(20) NOT NULL,
  `Tags` varchar(550),
  `FeaturedImage` varchar(550),
  `Published` tinyint(1) NOT NULL,
  `ScheduledFor` datetime DEFAULT NULL,
  `SEOTitle` varchar(255),
  `SEODescription` varchar(500),
  PRIMARY KEY (`PostID`),
  FOREIGN KEY (`UserID`) REFERENCES `BlogUser`(`UserId`)
);

CREATE TABLE `PostCategory` (
  `PostID` bigint(20) NOT NULL,
  `CategoryID` bigint(20) NOT NULL,
  PRIMARY KEY (`PostID`, `CategoryID`),
  FOREIGN KEY (`PostID`) REFERENCES `Post`(`PostID`),
  FOREIGN KEY (`CategoryID`) REFERENCES `Category`(`CategoryID`)
);

CREATE TABLE `Tag` (
  `TagID` bigint(20) NOT NULL AUTO_INCREMENT,
  `TagName` varchar(150) NOT NULL,
  PRIMARY KEY (`TagID`)
);

CREATE TABLE `Subscriber` (
  `SubscriberID` bigint(20) NOT NULL AUTO_INCREMENT,
  `Email` varchar(255) NOT NULL UNIQUE,
  `Name` varchar(255) NOT NULL,
  `SubscribedOn` datetime NOT NULL,
  `IsConfirmed` tinyint(1) DEFAULT 0,
  `Preferences` text,
  PRIMARY KEY (`SubscriberID`)
);

CREATE TABLE `LeadMagnet` (
  `LeadMagnetID` bigint(20) NOT NULL AUTO_INCREMENT,
  `MagnetName` varchar(255) NOT NULL,
  `MagnetFilePath` varchar(550) NOT NULL,
  `Description` text,
  PRIMARY KEY (`LeadMagnetID`)
);

CREATE TABLE `LeadMagnetDownload` (
  `DownloadID` bigint(20) NOT NULL AUTO_INCREMENT,
  `SubscriberID` bigint(20) NOT NULL,
  `LeadMagnetID` bigint(20) NOT NULL,
  `DownloadedOn` datetime NOT NULL,
  PRIMARY KEY (`DownloadID`),
  FOREIGN KEY (`SubscriberID`) REFERENCES `Subscriber`(`SubscriberID`),
  FOREIGN KEY (`LeadMagnetID`) REFERENCES `LeadMagnet`(`LeadMagnetID`)
);

-- User Events and Analytics
CREATE TABLE `UserEvents` (
  `EventID` bigint(20) NOT NULL AUTO_INCREMENT,
  `LogoIconPath` varchar(350) DEFAULT NULL,
  `EventTitle` varchar(350) DEFAULT NULL,
  `SessionTitle` varchar(350) DEFAULT NULL,
  `EventUrl` varchar(350) DEFAULT NULL,
  `EventDate` datetime DEFAULT NULL,
  `Type` varchar(50) DEFAULT NULL,
  `UserID` bigint(20) DEFAULT NULL,
  PRIMARY KEY (`EventID`),
  FOREIGN KEY (`UserID`) REFERENCES `BlogUser`(`UserId`)
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
  PRIMARY KEY (`SettingsID`),
  FOREIGN KEY (`UserID`) REFERENCES `BlogUser`(`UserId`)
);

CREATE TABLE `Widgets` (
  `WidgetID` int(11) NOT NULL AUTO_INCREMENT,
  `WidgetName` varchar(150) NOT NULL,
  `WidgetContent` varchar(550) NOT NULL,
  `UpdatedTime` datetime,
  `UserID` bigint(20) NOT NULL,
  PRIMARY KEY (`WidgetID`),
  FOREIGN KEY (`UserID`) REFERENCES `BlogUser`(`UserId`)
);

-- Analytics Tracking
CREATE TABLE `PostViews` (
  `ViewID` bigint(20) NOT NULL AUTO_INCREMENT,
  `PostID` bigint(20) NOT NULL,
  `ViewedOn` datetime NOT NULL,
  `ViewerIP` varchar(100),
  PRIMARY KEY (`ViewID`),
  FOREIGN KEY (`PostID`) REFERENCES `Post`(`PostID`)
);

CREATE TABLE `UserActions` (
  `ActionID` bigint(20) NOT NULL AUTO_INCREMENT,
  `UserID` bigint(20),
  `ActionType` varchar(100) NOT NULL,
  `ActionTimestamp` datetime NOT NULL,
  `Details` text,
  PRIMARY KEY (`ActionID`),
  FOREIGN KEY (`UserID`) REFERENCES `BlogUser`(`UserId`)
);

-- Email Campaigns
CREATE TABLE `Newsletter` (
  `NewsletterID` bigint(20) NOT NULL AUTO_INCREMENT,
  `Title` varchar(255) NOT NULL,
  `Content` longtext NOT NULL,
  `CreatedOn` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `ScheduledFor` datetime DEFAULT NULL,
  `Status` ENUM('draft', 'scheduled', 'sent') DEFAULT 'draft',
  PRIMARY KEY (`NewsletterID`)
);

CREATE TABLE `SubscriberNewsletter` (
  `ID` bigint(20) NOT NULL AUTO_INCREMENT,
  `SubscriberID` bigint(20) NOT NULL,
  `NewsletterID` bigint(20) NOT NULL,
  `SentOn` datetime DEFAULT NULL,
  `OpenedOn` datetime DEFAULT NULL,
  `ClickedOn` datetime DEFAULT NULL,
  PRIMARY KEY (`ID`),
  FOREIGN KEY (`SubscriberID`) REFERENCES `Subscriber`(`SubscriberID`),
  FOREIGN KEY (`NewsletterID`) REFERENCES `Newsletter`(`NewsletterID`)
);

CREATE TABLE `EmailSequence` (
  `SequenceID` bigint(20) NOT NULL AUTO_INCREMENT,
  `Name` varchar(255) NOT NULL,
  `Description` text,
  `CreatedOn` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `IsActive` tinyint(1) DEFAULT 1,
  PRIMARY KEY (`SequenceID`)
);

CREATE TABLE `EmailSequenceStep` (
  `StepID` bigint(20) NOT NULL AUTO_INCREMENT,
  `SequenceID` bigint(20) NOT NULL,
  `StepOrder` int(11) NOT NULL,
  `EmailSubject` varchar(255) NOT NULL,
  `EmailContent` longtext NOT NULL,
  `DelayDays` int(11) NOT NULL COMMENT 'Days after the previous email or start date',
  PRIMARY KEY (`StepID`),
  FOREIGN KEY (`SequenceID`) REFERENCES `EmailSequence`(`SequenceID`)
);

CREATE TABLE `SubscriberSequence` (
  `ID` bigint(20) NOT NULL AUTO_INCREMENT,
  `SubscriberID` bigint(20) NOT NULL,
  `SequenceID` bigint(20) NOT NULL,
  `StartedOn` datetime NOT NULL,
  `CurrentStepID` bigint(20),
  PRIMARY KEY (`ID`),
  FOREIGN KEY (`SubscriberID`) REFERENCES `Subscriber`(`SubscriberID`),
  FOREIGN KEY (`SequenceID`) REFERENCES `EmailSequence`(`SequenceID`),
  FOREIGN KEY (`CurrentStepID`) REFERENCES `EmailSequenceStep`(`StepID`)
);
