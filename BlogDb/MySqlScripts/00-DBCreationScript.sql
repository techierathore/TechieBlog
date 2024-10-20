CREATE TABLE UserRole (
    RoleId INT AUTO_INCREMENT PRIMARY KEY,
    RoleName VARCHAR(255) NOT NULL,
    RoleDesc VARCHAR(555)
);

CREATE TABLE BlogUser (
    UserId BIGINT AUTO_INCREMENT PRIMARY KEY,
    FirstName VARCHAR(255) NOT NULL,
    LastName VARCHAR(255) NOT NULL,
    EmailId VARCHAR(255) NOT NULL UNIQUE,
    LoginPass VARCHAR(255) NOT NULL,
    CreatedOn DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UpdatedOn DATETIME ON UPDATE CURRENT_TIMESTAMP,
    UserRoleId INT,
    IsConfirmed bit(1) DEFAULT b'0',
    ProfileImagePath VARCHAR(255),
    ProfileDescription TEXT,
    TwiiterUrl VARCHAR(255),
    LinkedInUrl VARCHAR(255),
    GitHubUrl VARCHAR(255),
    PodDescription VARCHAR(1050),
    SpeakDescription VARCHAR(1050)
    CONSTRAINT FK_UserRole FOREIGN KEY (UserRoleId) REFERENCES UserRole(RoleId)
);
