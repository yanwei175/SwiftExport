/*
  从 master 创建全新 SwiftExport 目标数据库。
  不覆盖、不删除、不复用任何已有数据库。
*/
USE master;
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @AllowCreateDatabase bit=1;
DECLARE @SourceDatabaseName sysname=N'dv';
DECLARE @TargetDatabaseName sysname=N'dv_next';

IF @AllowCreateDatabase<>1 THROW 52300,N'创建新数据库门禁未开启。',1;
IF NULLIF(@SourceDatabaseName,N'') IS NULL OR DB_ID(@SourceDatabaseName) IS NULL
    THROW 52301,N'源数据库不存在。',1;
IF NULLIF(@TargetDatabaseName,N'') IS NULL
    THROW 52302,N'必须填写 @TargetDatabaseName。',1;
IF @SourceDatabaseName=@TargetDatabaseName
    THROW 52303,N'源数据库与目标数据库不能同名。',1;
IF DB_ID(@TargetDatabaseName) IS NOT NULL
    THROW 52304,N'目标数据库已经存在；本脚本拒绝覆盖或复用。',1;
IF @@TRANCOUNT<>0
    THROW 52305,N'当前连接存在未结束事务。',1;

DECLARE @sql nvarchar(max)=N'CREATE DATABASE '+QUOTENAME(@TargetDatabaseName)+N';';
EXEC sys.sp_executesql @sql;

SELECT @SourceDatabaseName SourceDatabaseName,
       @TargetDatabaseName TargetDatabaseName,
       DB_ID(@TargetDatabaseName) TargetDatabaseId,
       SYSUTCDATETIME() CreatedAtUtc;
