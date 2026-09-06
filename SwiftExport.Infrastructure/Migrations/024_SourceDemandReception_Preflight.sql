/*
  来源需求接收迁移前只读检查。
  本脚本不修改数据，用于识别重复活动来源、销售行缺失和待补版本/客户/单位。
*/
SET NOCOUNT ON;

SELECT SourceType,SourceUid,SourceLineId,COUNT_BIG(1) AS ActiveDemandCount
FROM dbo.GrossDemand
WHERE IsDeleted=0 AND LifecycleStatus=0
GROUP BY SourceType,SourceUid,SourceLineId
HAVING COUNT_BIG(1)>1;

-- 新列已存在时显示当前保存值；旧结构预检不引用不存在的列，仍明确提示历史版本不可推断。
IF COL_LENGTH(N'dbo.GrossDemand',N'SourceVersion') IS NOT NULL
   AND COL_LENGTH(N'dbo.GrossDemand',N'CustomerId') IS NOT NULL
   AND COL_LENGTH(N'dbo.GrossDemand',N'UnitSnapshot') IS NOT NULL
    EXEC(N'
    SELECT d.Uid,d.SourceType,d.SourceUid,d.SourceNo,d.SourceLineId,
           CASE WHEN h.Uid IS NULL THEN N''缺少销售订单头'' WHEN l.Uid IS NULL THEN N''缺少销售订单行''
                WHEN h.CustomerId IS NULL THEN N''缺少客户身份''
                WHEN l.IsNonStandard=0 AND NULLIF(p.Unit,N'''') IS NULL THEN N''标准件缺少单位''
                ELSE N''可按销售头行身份映射'' END AS MappingResult,
           CASE WHEN d.SourceVersion IS NULL THEN N''无法确定历史接收版本，不自动回填'' ELSE N''已有来源版本'' END AS SourceVersionMappingResult,
           d.SourceVersion,h.Version AS CurrentSalesHeaderVersionForReview,
           d.CustomerId AS ExistingCustomerId,h.CustomerId AS CurrentCustomerIdForReview,
           d.UnitSnapshot AS ExistingUnit,p.Unit AS CurrentProductUnitForReview
    FROM dbo.GrossDemand d
    LEFT JOIN dbo.SalesOrderHeader h ON d.SourceType=N''SO'' AND h.Uid=d.SourceUid AND h.IsDeleted=0
    LEFT JOIN dbo.SalesOrderLine l ON l.Uid=d.SourceLineId AND l.SalesOrderId=d.SourceUid AND l.IsDeleted=0
    LEFT JOIN dbo.Product p ON p.Uid=l.ProductId AND p.IsDeleted=0
    WHERE d.IsDeleted=0 ORDER BY d.SourceType,d.SourceNo,d.SourceLineNumber,d.Uid;');
ELSE
    SELECT d.Uid,d.SourceType,d.SourceUid,d.SourceNo,d.SourceLineId,
           CASE WHEN h.Uid IS NULL THEN N'缺少销售订单头' WHEN l.Uid IS NULL THEN N'缺少销售订单行'
                WHEN h.CustomerId IS NULL THEN N'缺少客户身份'
                WHEN l.IsNonStandard=0 AND NULLIF(p.Unit,N'') IS NULL THEN N'标准件缺少单位'
                ELSE N'可按销售头行身份映射' END AS MappingResult,
           N'无法确定历史接收版本，不自动回填' AS SourceVersionMappingResult,
           CAST(NULL AS int) AS SourceVersion,h.Version AS CurrentSalesHeaderVersionForReview,
           CAST(NULL AS uniqueidentifier) AS ExistingCustomerId,h.CustomerId AS CurrentCustomerIdForReview,
           CAST(NULL AS nvarchar(100)) AS ExistingUnit,p.Unit AS CurrentProductUnitForReview
    FROM dbo.GrossDemand d
    LEFT JOIN dbo.SalesOrderHeader h ON d.SourceType=N'SO' AND h.Uid=d.SourceUid AND h.IsDeleted=0
    LEFT JOIN dbo.SalesOrderLine l ON l.Uid=d.SourceLineId AND l.SalesOrderId=d.SourceUid AND l.IsDeleted=0
    LEFT JOIN dbo.Product p ON p.Uid=l.ProductId AND p.IsDeleted=0
    WHERE d.IsDeleted=0 ORDER BY d.SourceType,d.SourceNo,d.SourceLineNumber,d.Uid;
