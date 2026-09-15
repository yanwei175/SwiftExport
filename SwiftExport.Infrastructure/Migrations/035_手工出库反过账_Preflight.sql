-- 只读部署预检：不连接配置、不建表、不改约束、不回填任何历史事实。
-- 人工核对后才可部署；缺失字段或约束须另行审核增量，禁止修改已执行的 033/034。
SELECT v.TableName,v.ColumnName,v.ExpectedType,t.name AS ActualType,c.max_length,c.precision,c.scale,c.is_nullable,
       CASE WHEN c.column_id IS NULL THEN N'缺失：阻断部署，另行审核增量' ELSE N'存在：人工核对预期类型与约束' END AS Result
FROM (VALUES
 (N'OutboundDocument',N'ReversedDate',N'可空 datetime2'),
 (N'OutboundDocument',N'ReversedBy',N'可空 nvarchar，满足操作人名称长度'),
 (N'OutboundDocument',N'ReverseReason',N'可空 nvarchar，至少 500 字'),
 (N'OutboundDocument',N'IssueReason',N'可空 nvarchar(500)，历史空值允许'),
 (N'OutboundDocument',N'TriggerPackingListId',N'可空 uniqueidentifier'),
 (N'OutboundDocument',N'PostingStatus',N'状态支持 0/1/8'),
 (N'InventoryLedgerEntry',N'ReversalOfLedgerId',N'可空 uniqueidentifier'),
 (N'InventoryLedgerEntry',N'TransactionType',N'int，支持 22/900'),
 (N'InventoryBalance',N'OnHandQty',N'decimal(18,4)'),
 (N'InventoryBalance',N'UnitSnapshot',N'nvarchar，历史单位快照'),
 (N'BusinessOperationLog',N'RelatedObjectsJson',N'nvarchar(max)')) v(TableName,ColumnName,ExpectedType)
LEFT JOIN sys.columns c ON c.object_id=OBJECT_ID(N'dbo.'+v.TableName) AND c.name=v.ColumnName
LEFT JOIN sys.types t ON t.user_type_id=c.user_type_id;

-- 状态 8、类型 900、正向恢复及反向关系约束必须可用且受信任，不以约束名字代替定义核查。
SELECT OBJECT_NAME(parent_object_id) AS TableName,name,definition,is_disabled,is_not_trusted
FROM sys.check_constraints WHERE parent_object_id IN(OBJECT_ID(N'dbo.OutboundDocument'),OBJECT_ID(N'dbo.InventoryLedgerEntry'));
-- 重点确认 ReversalOfLedgerId 的非空唯一索引、命令日志唯一性以及余额库存键唯一性。
SELECT OBJECT_NAME(i.object_id) AS TableName,i.name,i.is_unique,i.is_disabled,i.filter_definition,
       c.name AS ColumnName,ic.key_ordinal,ic.is_included_column
FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
WHERE i.object_id IN(OBJECT_ID(N'dbo.InventoryLedgerEntry'),OBJECT_ID(N'dbo.InventoryBalance'),OBJECT_ID(N'dbo.BusinessOperationLog'))
ORDER BY TableName,i.name,ic.key_ordinal;

-- 只报风险，不按单号或 SKU 补关联。此项依赖既有 031/033 表结构，报缺列错误也属于阻断，不能视为通过。
IF OBJECT_ID(N'dbo.OutboundDocument') IS NOT NULL AND OBJECT_ID(N'dbo.OutboundDocumentLine') IS NOT NULL
 AND OBJECT_ID(N'dbo.InventoryLedgerEntry') IS NOT NULL AND COL_LENGTH(N'dbo.OutboundDocument',N'TriggerPackingListId') IS NOT NULL
EXEC sys.sp_executesql N'
SELECT h.Uid,h.DocumentNo,l.Uid AS LineId,N''缺失或不唯一的原类型22流水'' AS Risk
FROM dbo.OutboundDocument h JOIN dbo.OutboundDocumentLine l ON l.OutboundDocumentId=h.Uid AND l.IsDeleted=0
OUTER APPLY(SELECT COUNT_BIG(*) AS Matches FROM dbo.InventoryLedgerEntry e
 WHERE e.SourceDocumentId=h.Uid AND e.SourceLineId=l.Uid AND e.CommandItemId=l.CommandItemId
 AND e.SourceDocumentType=N''OutboundDocument'' AND e.TransactionType=22 AND e.IsDeleted=0
 AND e.WarehouseSiteId=h.WarehouseSiteId AND e.ProductId=l.ProductId AND e.QuantityDelta=-l.Quantity
 AND e.UnitSnapshot=l.UnitSnapshot AND e.ReversalOfLedgerId IS NULL AND e.ReplacementOfLedgerId IS NULL
 AND e.CorrectionGroupId IS NULL) f
WHERE h.IsDeleted=0 AND h.LifecycleStatus=0 AND h.PostingStatus=1 AND h.DocumentType=22
 AND h.TriggerPackingListId IS NULL AND f.Matches<>1;
SELECT e.SourceDocumentId,e.SourceLineId,e.Uid,N''原流水已有反向或替代关联'' AS Risk
FROM dbo.InventoryLedgerEntry e WHERE e.TransactionType=22 AND EXISTS
 (SELECT 1 FROM dbo.InventoryLedgerEntry r WHERE r.ReversalOfLedgerId=e.Uid OR r.ReplacementOfLedgerId=e.Uid);';

-- 单次命令的最终证明由服务端完成，以上只读检查不构成反向许可，也不预留库存。
-- 真正 SQL Server 同键并发、失败回滚及 UI 连通验收需要当次授权，未在生成脚本时执行。
