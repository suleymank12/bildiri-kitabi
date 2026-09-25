-- Runs once per `docker compose up` (db-init service) and is safe to run again: creates the database and the
-- application login, which owns the database so the API can apply its migrations. The API never uses `sa`.
-- $(AppPassword) is passed by sqlcmd from APP_DB_PASSWORD in .env.

IF DB_ID(N'BildiriKitabi') IS NULL
    CREATE DATABASE [BildiriKitabi];
GO

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'bildiri_app')
    EXEC (N'CREATE LOGIN [bildiri_app] WITH PASSWORD = N''$(AppPassword)'', DEFAULT_DATABASE = [BildiriKitabi], CHECK_POLICY = ON');
ELSE
    EXEC (N'ALTER LOGIN [bildiri_app] WITH PASSWORD = N''$(AppPassword)''');
GO

USE [BildiriKitabi];
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'bildiri_app')
    CREATE USER [bildiri_app] FOR LOGIN [bildiri_app];
GO

ALTER ROLE [db_owner] ADD MEMBER [bildiri_app];
GO

PRINT N'BildiriKitabi veritabanı ve bildiri_app girişi hazır.';
