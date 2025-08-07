INSERT INTO UserRole (RoleName, RoleDesc) 
VALUES ('Admin', 'Administrator role with full access');

INSERT INTO UserRole (RoleName, RoleDesc) 
VALUES ('Blogger', 'Blogger role with access to create and manage blog posts');

INSERT INTO UserRole (RoleName, RoleDesc) 
VALUES ('Subscriber', 'Subscriber role with access to read and comment on blog posts');


INSERT INTO BlogUser 
(FirstName, LastName, EmailId, LoginPass, CreatedOn, UpdatedOn, UserRoleId, ProfileImagePath, ProfileDescription, TwiiterUrl, LinkedInUrl, GitHubUrl, PodDescription, SpeakDescription)
VALUES 
('S Ravi', 'Kumar', 'Ravi@techieblog.com', 'admin_password', NOW(), NOW(), 
 1, 
 NULL, NULL, NULL, NULL, NULL, NULL, NULL);