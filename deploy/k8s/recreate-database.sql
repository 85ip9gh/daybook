IF DB_ID(N'Ledger') IS NOT NULL
BEGIN
    ALTER DATABASE Ledger SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE Ledger;
END;
GO
CREATE DATABASE Ledger;
GO
IF SUSER_ID(N'daybook_app') IS NULL
    CREATE LOGIN daybook_app WITH PASSWORD = N'$(app_password)';
ELSE
    ALTER LOGIN daybook_app WITH PASSWORD = N'$(app_password)';
GO
USE Ledger;
CREATE USER daybook_app FOR LOGIN daybook_app;
ALTER ROLE db_owner ADD MEMBER daybook_app;
GO
