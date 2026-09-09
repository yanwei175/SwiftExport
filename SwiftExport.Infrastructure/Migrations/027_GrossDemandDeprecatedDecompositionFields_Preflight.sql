/*
  来源需求旧拆解字段退出只读预检。
  本脚本不修改数据、不删除列；仅报告历史解释缺口和数据库对象依赖。
*/
SET NOCOUNT ON;

SELECT columnInfo.[name] AS [ColumnName],typeInfo.[name] AS [SqlType],columnInfo.[is_nullable] AS [IsNullable],
       defaultInfo.[name] AS [DefaultConstraintName]
FROM sys.columns columnInfo
INNER JOIN sys.types typeInfo ON typeInfo.[user_type_id]=columnInfo.[user_type_id]
LEFT JOIN sys.default_constraints defaultInfo
    ON defaultInfo.[parent_object_id]=columnInfo.[object_id] AND defaultInfo.[parent_column_id]=columnInfo.[column_id]
WHERE columnInfo.[object_id]=OBJECT_ID(N'dbo.GrossDemand')
  AND columnInfo.[name] IN (N'DecompositionStatus',N'DecompositionRevision',N'DecomposedAt',N'DecomposedBy')
ORDER BY columnInfo.[column_id];

SELECT objectInfo.[name] AS [DependentObject],objectInfo.[type_desc] AS [ObjectType],
       expressionInfo.[referenced_minor_name] AS [ReferencedColumn]
FROM sys.sql_expression_dependencies expressionInfo
INNER JOIN sys.objects objectInfo ON objectInfo.[object_id]=expressionInfo.[referencing_id]
WHERE expressionInfo.[referenced_id]=OBJECT_ID(N'dbo.GrossDemand')
  AND expressionInfo.[referenced_minor_name] IN (N'DecompositionStatus',N'DecompositionRevision',N'DecomposedAt',N'DecomposedBy')
ORDER BY objectInfo.[name],expressionInfo.[referenced_minor_name];

SELECT indexInfo.[name] AS [IndexName],columnInfo.[name] AS [ReferencedColumn],indexColumn.[is_included_column] AS [IsIncluded]
FROM sys.indexes indexInfo
INNER JOIN sys.index_columns indexColumn
    ON indexColumn.[object_id]=indexInfo.[object_id] AND indexColumn.[index_id]=indexInfo.[index_id]
INNER JOIN sys.columns columnInfo
    ON columnInfo.[object_id]=indexColumn.[object_id] AND columnInfo.[column_id]=indexColumn.[column_id]
WHERE indexInfo.[object_id]=OBJECT_ID(N'dbo.GrossDemand')
  AND columnInfo.[name] IN (N'DecompositionStatus',N'DecompositionRevision',N'DecomposedAt',N'DecomposedBy')
ORDER BY indexInfo.[name],indexColumn.[key_ordinal],indexColumn.[index_column_id];

IF COL_LENGTH(N'dbo.GrossDemand',N'DecompositionStatus') IS NOT NULL
   AND COL_LENGTH(N'dbo.GrossDemand',N'DecompositionRevision') IS NOT NULL
   AND COL_LENGTH(N'dbo.GrossDemand',N'DecomposedAt') IS NOT NULL
   AND COL_LENGTH(N'dbo.GrossDemand',N'DecomposedBy') IS NOT NULL
BEGIN
    EXEC sys.sp_executesql N'
        SELECT demand.Uid,demand.SourceType,demand.SourceUid,demand.SourceLineId,
               demand.DecompositionStatus,demand.DecompositionRevision,demand.DecomposedAt,demand.DecomposedBy,
               COALESCE(history.NetDemandCount,0) AS NetDemandCount,
               COALESCE(history.LatestRevision,0) AS LatestMaterialRevision,
               CASE
                   WHEN demand.DecompositionRevision=0 AND COALESCE(history.NetDemandCount,0)=0 THEN N''LEGACY_EMPTY_MATCHES_HISTORY''
                   WHEN demand.DecompositionRevision=COALESCE(history.LatestRevision,0) THEN N''LEGACY_REVISION_MATCHES_HISTORY''
                   ELSE N''HISTORY_MAPPING_REQUIRES_REVIEW''
               END AS MappingDisposition
        FROM dbo.GrossDemand demand
        OUTER APPLY(
            SELECT COUNT(1) AS NetDemandCount,MAX(material.DecompositionRevision) AS LatestRevision
            FROM dbo.NetDemand material
            WHERE material.GrossDemandId=demand.Uid
        ) history
        WHERE demand.DecompositionStatus<>0 OR demand.DecompositionRevision<>0
           OR demand.DecomposedAt IS NOT NULL OR demand.DecomposedBy IS NOT NULL
           OR COALESCE(history.NetDemandCount,0)>0
        ORDER BY demand.SourceType,demand.SourceUid,demand.SourceLineId,demand.Uid;';
END
ELSE
BEGIN
    SELECT N'SCHEMA_ALREADY_NARROWED_OR_INCOMPLETE' AS [Disposition],
           N'至少一个旧拆解字段不存在；无需也不得按旧结构执行历史映射。' AS [Message];
END;
