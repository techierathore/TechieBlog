-- Stored Procedures for UserRole

CREATE PROCEDURE InsertUserRole (
    IN RoleName VARCHAR(255),
    IN RoleDesc VARCHAR(555)
)
BEGIN
    INSERT INTO UserRole (RoleName, RoleDesc) 
    VALUES (RoleName, RoleDesc);
END;

CREATE PROCEDURE UpdateUserRole (
    IN RoleId INT,
    IN RoleName VARCHAR(255),
    IN RoleDesc VARCHAR(555)
)
BEGIN
    UPDATE UserRole 
    SET RoleName = RoleName, RoleDesc = RoleDesc
    WHERE RoleId = RoleId;
END;

CREATE PROCEDURE SelectUserRoleById (
    IN RoleId INT
)
BEGIN
    SELECT * FROM UserRole WHERE RoleId = RoleId;
END;

CREATE PROCEDURE SelectAllUserRoles ()
BEGIN
    SELECT * FROM UserRole;
END;

CREATE PROCEDURE DeleteUserRole (
    IN RoleId INT
)
BEGIN
    DELETE FROM UserRole WHERE RoleId = RoleId;
END;
