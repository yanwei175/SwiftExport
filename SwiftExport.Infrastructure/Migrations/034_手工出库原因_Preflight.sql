-- 只读人工预检；不回填原因，不把历史空值视为可修改已过账事实的授权。
SELECT c.name,TYPE_NAME(c.user_type_id) AS TypeName,c.max_length,c.is_nullable,c.is_computed,
       dc.definition AS DefaultValue
FROM sys.columns c LEFT JOIN sys.default_constraints dc ON dc.object_id=c.default_object_id
WHERE c.object_id=OBJECT_ID(N'dbo.OutboundDocument') AND c.name IN(N'IssueReason',N'Remark',N'TriggerPackingListId');
SELECT CASE WHEN COL_LENGTH(N'dbo.OutboundDocument',N'IssueReason') IS NULL
    THEN N'待增量：034 将新增可空 nvarchar(500)，不回填历史。'
    ELSE N'字段已存在：核对类型、长度、可空、非计算且无默认值。' END AS Result;
SELECT name,definition,is_disabled,is_not_trusted FROM sys.check_constraints
WHERE parent_object_id=OBJECT_ID(N'dbo.OutboundDocument');
IF COL_LENGTH(N'dbo.OutboundDocument',N'IssueReason') IS NOT NULL
    EXEC sys.sp_executesql N'SELECT PostingStatus,COUNT_BIG(*) AS EmptyReasonCount
        FROM dbo.OutboundDocument WHERE IsDeleted=0 AND DocumentType=22
        AND (IssueReason IS NULL OR LEN(LTRIM(RTRIM(IssueReason)))=0) GROUP BY PostingStatus;';
-- 上述空白统计仅识别 SQL 空格；Application 的必填校验同时拒绝制表符、换行等空白。
