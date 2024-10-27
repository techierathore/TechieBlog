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
