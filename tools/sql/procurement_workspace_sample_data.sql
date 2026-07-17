SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() NOT LIKE N'SwiftExportProcurementTest[_]%'
    THROW 51010, N'示例数据脚本仅允许在 SwiftExportProcurementTest_* 一次性测试库执行。', 1;
IF OBJECT_ID(N'dbo.PurchaseOrderHead',N'U') IS NULL OR OBJECT_ID(N'dbo.FollowUpLog',N'U') IS NULL
    THROW 51011, N'请先执行 009 迁移。', 1;

DECLARE @HeadId uniqueidentifier=(SELECT TOP(1) Uid FROM dbo.PurchaseOrderHead WHERE IsDeleted=0 ORDER BY Uid);
DECLARE @LineId uniqueidentifier=(SELECT TOP(1) Uid FROM dbo.PurchaseOrderLine WHERE PurchaseOrderId=@HeadId AND IsDeleted=0 ORDER BY LineNumber,Uid);

IF @LineId IS NULL THROW 51012, N'测试库中没有可用于示例反馈的采购行。', 1;

DECLARE @CommandId uniqueidentifier=NEWID();
INSERT dbo.FollowUpLog
(Uid,Version,IsDeleted,CreatedDate,CreatedBy,RefLineId,NoteContent,RefLineType,Sequence,ArrangementStatus,ReadyQty,ExpectedReadyDate,SupplierReportedDate,IsCorrection,CorrectionReason,CommandId)
VALUES(NEWID(),1,0,SYSDATETIME(),N'sample',@LineId,N'供应商已安排生产',N'PurchaseOrderLine',1,2,0,DATEADD(day,14,SYSDATETIME()),SYSDATETIME(),0,NULL,@CommandId);
