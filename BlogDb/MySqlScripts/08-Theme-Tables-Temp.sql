USE `TechieBlog`;

-- Table for storing available themes
CREATE TABLE `Theme` (
  `ThemeID` bigint(20) NOT NULL AUTO_INCREMENT,
  `ThemeName` varchar(255) NOT NULL,
  `Description` text,
  `PreviewImagePath` varchar(550) DEFAULT NULL,
  `IsActive` tinyint(1) DEFAULT 1,
  PRIMARY KEY (`ThemeID`)
);

-- Table for defining theme options (e.g., colors, fonts)
CREATE TABLE `ThemeOption` (
  `OptionID` bigint(20) NOT NULL AUTO_INCREMENT,
  `ThemeID` bigint(20) NOT NULL,
  `OptionName` varchar(255) NOT NULL,
  `OptionType` ENUM('color', 'font', 'image', 'spacing', 'other') NOT NULL,
  `DefaultValue` varchar(255),
  PRIMARY KEY (`OptionID`),
  FOREIGN KEY (`ThemeID`) REFERENCES `Theme`(`ThemeID`)
);

-- Table for storing user-selected theme options (for customization)
CREATE TABLE `UserThemeSetting` (
  `SettingID` bigint(20) NOT NULL AUTO_INCREMENT,
  `UserID` bigint(20) NOT NULL,
  `ThemeID` bigint(20) NOT NULL,
  `OptionID` bigint(20) NOT NULL,
  `CustomValue` varchar(255),
  PRIMARY KEY (`SettingID`),
  FOREIGN KEY (`UserID`) REFERENCES `BlogUser`(`UserId`),
  FOREIGN KEY (`ThemeID`) REFERENCES `Theme`(`ThemeID`),
  FOREIGN KEY (`OptionID`) REFERENCES `ThemeOption`(`OptionID`)
);

-- Table for storing assets (e.g., CSS, images) related to themes
CREATE TABLE `ThemeAsset` (
  `AssetID` bigint(20) NOT NULL AUTO_INCREMENT,
  `ThemeID` bigint(20) NOT NULL,
  `AssetType` ENUM('css', 'js', 'image', 'font') NOT NULL,
  `AssetPath` varchar(550) NOT NULL,
  `CreatedOn` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`AssetID`),
  FOREIGN KEY (`ThemeID`) REFERENCES `Theme`(`ThemeID`)
);

-- Optional: Table for tracking user preferences for blog themes
CREATE TABLE `UserPreferredTheme` (
  `PreferenceID` bigint(20) NOT NULL AUTO_INCREMENT,
  `UserID` bigint(20) NOT NULL,
  `ThemeID` bigint(20) NOT NULL,
  `AppliedOn` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`PreferenceID`),
  FOREIGN KEY (`UserID`) REFERENCES `BlogUser`(`UserId`),
  FOREIGN KEY (`ThemeID`) REFERENCES `Theme`(`ThemeID`)
);
