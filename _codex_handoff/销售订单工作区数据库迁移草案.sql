/*
  销售订单工作区数据库迁移草案
  重要：本脚本仅供后端实现阶段审查，不应整段直接执行。
  实际表名、列名、软删除类型和既有约束必须先与当前测试库核对。
*/

SET XACT_ABORT ON;

/* 一、只读预检查：任何结果都必须先人工处理。 */
SELECT Uid, COUNT(*) AS DuplicateCount
FROM SalesOrderLine
WHERE IsDeleted = 0
GROUP BY Uid
HAVING Uid IS NULL OR COUNT(*) > 1;

SELECT SalesOrderId, LineNumber, COUNT(*) AS DuplicateCount
FROM SalesOrderLine
WHERE IsDeleted = 0
GROUP BY SalesOrderId, LineNumber
HAVING SalesOrderId IS NULL OR COUNT(*) > 1;

SELECT Uid, IsNonStandard, ProductId, Sku
FROM SalesOrderLine
WHERE IsDeleted = 0
  AND ((IsNonStandard = 0 AND ProductId IS NULL)
    OR (IsNonStandard = 1 AND ProductId IS NOT NULL)
    OR (IsNonStandard = 1 AND Sku NOT LIKE N'非标件-%'));

SELECT SourceType, SourceLineId, COUNT(*) AS DuplicateCount
FROM IndependentDemand
WHERE IsDeleted = 0
GROUP BY SourceType, SourceLineId
HAVING COUNT(*) > 1;

/*
  二、兼容性新增示例。具体类型必须以实体和数据库现状为准。

BEGIN TRANSACTION;

ALTER TABLE SalesOrderHeader
ADD DocumentStatus int NOT NULL
    CONSTRAINT DF_SalesOrderHeader_DocumentStatus DEFAULT (0);

UPDATE SalesOrderHeader
SET ConfirmationStatus = 0
WHERE ConfirmationStatus IS NULL;

ALTER TABLE SalesOrderLine ADD ProductId uniqueidentifier NULL;
ALTER TABLE SalesOrderLine ADD CustomerProductId uniqueidentifier NULL;

CREATE UNIQUE INDEX UX_SalesOrderLine_Uid
ON SalesOrderLine(Uid);

CREATE UNIQUE INDEX UX_SalesOrderLine_Active_Order_LineNumber
ON SalesOrderLine(SalesOrderId, LineNumber)
WHERE IsDeleted = 0;

CREATE UNIQUE INDEX UX_SalesOrderLine_Active_NonStandard_Sku
ON SalesOrderLine(Sku)
WHERE IsNonStandard = 1 AND IsDeleted = 0;

CREATE UNIQUE INDEX UX_IndependentDemand_Active_SourceLine
ON IndependentDemand(SourceType, SourceLineId)
WHERE IsDeleted = 0;

-- 核对影响行数和约束后，测试环境可 COMMIT；否则 ROLLBACK。
ROLLBACK TRANSACTION;
*/

/*
  三、最终删列阶段：仅在 Application、Repository、旧客户端全部迁移完成后另行执行。
  禁止在本阶段执行。

ALTER TABLE SalesOrderHeader DROP COLUMN OrderStatus;
ALTER TABLE SalesOrderHeader DROP COLUMN ApprovalStatus;
ALTER TABLE SalesOrderHeader DROP COLUMN VersionNo;
ALTER TABLE SalesOrderHeader DROP COLUMN IsLocked;
*/
