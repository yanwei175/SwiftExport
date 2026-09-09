/*
  只读预检：不推断、不回填历史采用版本。
  脚本可在增量结构执行前运行；缺列时只报告前置缺口，不引用尚不存在的列。
*/
IF COL_LENGTH(N'dbo.GrossDemand', N'DemandBasisFingerprint') IS NULL
   OR COL_LENGTH(N'dbo.NetDemand', N'SourceDemandVersionSnapshot') IS NULL
   OR COL_LENGTH(N'dbo.NetDemand', N'SourceVersionSnapshot') IS NULL
   OR COL_LENGTH(N'dbo.NetDemand', N'SourceDemandBasisFingerprintSnapshot') IS NULL
BEGIN
    SELECT N'SCHEMA_NOT_APPLIED' AS PreviewClassification,
           N'请先审核并执行 026_MaterialDemandEstablishment_AdditiveSchema.sql；本预检未修改数据库。' AS Message;
    RETURN;
END;

/* 使用动态 SQL 避免 SQL Server 在缺列分支仍提前绑定新列名。 */
EXEC sys.sp_executesql N'
SELECT
    gd.Uid AS GrossDemandId,
    gd.SourceType,
    gd.SourceNo,
    gd.SourceLineId,
    gd.Version AS CurrentSourceDemandVersion,
    gd.SourceVersion AS CurrentSourceVersion,
    gd.DemandBasisFingerprint AS CurrentDemandBasisFingerprint,
    COUNT(nd.Uid) AS ActiveMaterialDemandCount,
    MIN(nd.SourceDemandVersionSnapshot) AS AdoptedSourceDemandVersion,
    MIN(nd.SourceVersionSnapshot) AS AdoptedSourceVersion,
    MIN(nd.SourceDemandBasisFingerprintSnapshot) AS AdoptedDemandBasisFingerprint,
    CASE
        WHEN COUNT(nd.Uid)=0 THEN N''NoActiveMaterialDemand''
        WHEN gd.DemandBasisFingerprint IS NULL OR MIN(nd.SourceDemandBasisFingerprintSnapshot) IS NULL
            THEN N''ExistingActiveWithUnknownBasis''
        WHEN MIN(nd.SourceDemandBasisFingerprintSnapshot)=gd.DemandBasisFingerprint
         AND MAX(nd.SourceDemandBasisFingerprintSnapshot)=gd.DemandBasisFingerprint
            THEN N''ExistingActiveWithMatchingBasis''
        ELSE N''ExistingActiveWithChangedSource''
    END AS PreviewClassification
FROM dbo.GrossDemand gd
LEFT JOIN dbo.NetDemand nd
  ON nd.GrossDemandId=gd.Uid AND nd.LifecycleStatus=0 AND nd.IsDeleted=0
WHERE gd.IsDeleted=0
GROUP BY gd.Uid,gd.SourceType,gd.SourceNo,gd.SourceLineId,gd.Version,gd.SourceVersion,gd.DemandBasisFingerprint
ORDER BY gd.SourceNo,gd.SourceLineId;';
