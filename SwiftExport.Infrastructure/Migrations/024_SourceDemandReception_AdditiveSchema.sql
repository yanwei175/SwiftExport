/*
  来源需求接收增量结构草案。请先运行同编号 Preflight；本仓库任务不会自动执行本脚本。
  历史 SourceVersion 不自动用当前销售版本回填，避免把无法证明的历史接收时点冒充为事实。
*/
SET XACT_ABORT ON;

-- 重复活动来源身份会使幂等接收无法判定唯一目标，必须先按预检结果人工处置。
IF EXISTS(
    SELECT 1 FROM dbo.GrossDemand WHERE IsDeleted=0 AND LifecycleStatus=0
    GROUP BY SourceType,SourceUid,SourceLineId HAVING COUNT_BIG(1)>1)
    THROW 50024,N'存在重复活动来源需求，请先运行 024_SourceDemandReception_Preflight.sql 并完成处置。',1;

BEGIN TRANSACTION;

IF COL_LENGTH(N'dbo.GrossDemand',N'SourceVersion') IS NULL
    ALTER TABLE dbo.GrossDemand ADD SourceVersion int NULL;
IF COL_LENGTH(N'dbo.GrossDemand',N'CustomerId') IS NULL
    ALTER TABLE dbo.GrossDemand ADD CustomerId uniqueidentifier NULL;
IF COL_LENGTH(N'dbo.GrossDemand',N'UnitSnapshot') IS NULL
    ALTER TABLE dbo.GrossDemand ADD UnitSnapshot nvarchar(100) NULL;

IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.GrossDemand') AND name=N'UX_GrossDemand_SourceLine')
    DROP INDEX UX_GrossDemand_SourceLine ON dbo.GrossDemand;
CREATE UNIQUE INDEX UX_GrossDemand_SourceLine
ON dbo.GrossDemand(SourceType,SourceUid,SourceLineId)
WHERE IsDeleted=0 AND LifecycleStatus=0;

COMMIT TRANSACTION;
