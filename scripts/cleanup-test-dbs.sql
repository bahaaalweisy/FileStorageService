DECLARE @sql NVARCHAR(MAX) = '';
SELECT @sql = @sql + 'ALTER DATABASE [' + name + '] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [' + name + '];' + CHAR(10)
FROM sys.databases
WHERE name LIKE 'FileStorageIntegrationTests_%';

EXEC sp_executesql @sql;
