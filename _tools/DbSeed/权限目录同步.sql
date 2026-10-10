-- 自动生成：PermissionCatalog.All（116 项），实体字段来自 EntityMetadataCache。
-- 生成入口：_tools/DbSeed/同步权限目录.ps1；不要手改目录项，代码目录改变后重新生成。
-- 本文件不含连接信息。请在已确认的目标库一次执行完整脚本；生成不等于执行。
-- 只同步定义，不创建账号、不修改用户/角色授权、不删除目录外定义、不迁移结构。
-- 需要当前 PG 权限结构及内置 gen_random_uuid()；异常时整笔事务失败，不自动重试。
BEGIN TRANSACTION ISOLATION LEVEL SERIALIZABLE;
SET LOCAL standard_conforming_strings = on;
-- 在首个数据快照前沿用角色→权限顺序，协调已有 RBAC 与目录同步用例。
LOCK TABLE "public"."sys_role" IN SHARE ROW EXCLUSIVE MODE;
LOCK TABLE "public"."sys_permission" IN SHARE ROW EXCLUSIVE MODE;

CREATE TEMP TABLE swift_export_permission_catalog (
    permission_key text COLLATE "C" PRIMARY KEY,
    display_name text COLLATE "C" NOT NULL,
    group_key text COLLATE "C" NOT NULL,
    sort_order integer NOT NULL
) ON COMMIT DROP;
INSERT INTO pg_temp.swift_export_permission_catalog (permission_key, display_name, group_key, sort_order)
VALUES
    ('Customers.Customer.Create', '客户创建', 'Customers', 1),
    ('Customers.Customer.Delete', '客户删除', 'Customers', 2),
    ('Customers.Customer.Edit', '客户编辑', 'Customers', 3),
    ('Customers.Customer.View', '客户查看', 'Customers', 4),
    ('Customers.PoConfig.Create', '客户单头配置创建', 'Customers', 5),
    ('Customers.PoConfig.Delete', '客户单头配置删除', 'Customers', 6),
    ('Customers.PoConfig.Edit', '客户单头配置编辑', 'Customers', 7),
    ('Customers.PoConfig.View', '客户单头配置查看', 'Customers', 8),
    ('Customers.Products.Create', '客户产品创建', 'Customers', 9),
    ('Customers.Products.Delete', '客户产品删除', 'Customers', 10),
    ('Customers.Products.Edit', '客户产品编辑', 'Customers', 11),
    ('Customers.Products.View', '客户产品查看', 'Customers', 12),
    ('DemandPlanning.Material.Establish', '物料需求建立', 'DemandPlanning', 13),
    ('DemandPlanning.Material.Rollback', '物料需求回退', 'DemandPlanning', 14),
    ('DemandPlanning.Material.SupplierSuggestion.Manage', '建议供应商管理', 'DemandPlanning', 15),
    ('DemandPlanning.Material.View', '物料需求查看', 'DemandPlanning', 16),
    ('DemandPlanning.Source.Manual.Manage', '手工需求管理', 'DemandPlanning', 17),
    ('DemandPlanning.Source.View', '来源需求查看', 'DemandPlanning', 18),
    ('Inventory.Balance.View', '库存余额与流水查看', 'Inventory', 19),
    ('Inventory.Inbound.Create', '入库单创建', 'Inventory', 20),
    ('Inventory.Inbound.Delete', '入库单删除', 'Inventory', 21),
    ('Inventory.Inbound.Edit', '入库单编辑', 'Inventory', 22),
    ('Inventory.Inbound.Post', '入库单过账', 'Inventory', 23),
    ('Inventory.Inbound.Reverse', '入库单反向', 'Inventory', 24),
    ('Inventory.Inbound.View', '入库单查看', 'Inventory', 25),
    ('Inventory.Outbound.Create', '出库单创建', 'Inventory', 26),
    ('Inventory.Outbound.Delete', '出库单删除', 'Inventory', 27),
    ('Inventory.Outbound.Edit', '出库单编辑', 'Inventory', 28),
    ('Inventory.Outbound.Post', '出库单过账', 'Inventory', 29),
    ('Inventory.Outbound.Reverse', '出库单反向', 'Inventory', 30),
    ('Inventory.Outbound.View', '出库单查看', 'Inventory', 31),
    ('Inventory.Warehouse.Manage', '仓库管理', 'Inventory', 32),
    ('Inventory.Warehouse.View', '仓库查看', 'Inventory', 33),
    ('MasterData.Eligibility.Manage', '主数据执行资格管理', 'MasterData', 34),
    ('Sales.Order.CancelConfirmation', '销售订单取消确认', 'Sales', 35),
    ('Sales.Order.Confirm', '销售订单确认', 'Sales', 36),
    ('Sales.Order.Delete', '销售订单删除', 'Sales', 37),
    ('Sales.Order.Lifecycle', '销售订单生命周期', 'Sales', 38),
    ('Sales.Order.Maintain', '销售订单草稿维护', 'Sales', 39),
    ('Procurement.Lookup.View', '采购候选查看', 'Procurement', 40),
    ('Procurement.Order.Confirm', '采购订单确认', 'Procurement', 41),
    ('Procurement.Order.Create', '采购订单创建', 'Procurement', 42),
    ('Procurement.Order.Edit', '采购订单编辑', 'Procurement', 43),
    ('Procurement.Order.Read', '采购订单读取', 'Procurement', 44),
    ('Procurement.Order.Void', '采购订单作废', 'Procurement', 45),
    ('Procurement.Progress.Report', '采购跟进报告', 'Procurement', 46),
    ('Procurement.Progress.View', '采购跟进查看', 'Procurement', 47),
    ('Products.Bom.Create', 'BOM创建', 'Products', 48),
    ('Products.Bom.Delete', 'BOM删除', 'Products', 49),
    ('Products.Bom.Edit', 'BOM编辑', 'Products', 50),
    ('Products.Bom.View', 'BOM查看', 'Products', 51),
    ('Products.Category.Create', '产品分类创建', 'Products', 52),
    ('Products.Category.Delete', '产品分类删除', 'Products', 53),
    ('Products.Category.Edit', '产品分类编辑', 'Products', 54),
    ('Products.Category.View', '产品分类查看', 'Products', 55),
    ('Products.Color.Create', '产品颜色创建', 'Products', 56),
    ('Products.Color.Delete', '产品颜色删除', 'Products', 57),
    ('Products.Color.Edit', '产品颜色编辑', 'Products', 58),
    ('Products.Color.View', '产品颜色查看', 'Products', 59),
    ('Products.Master.Create', '主型号创建', 'Products', 60),
    ('Products.Master.Delete', '主型号删除', 'Products', 61),
    ('Products.Master.Edit', '主型号编辑', 'Products', 62),
    ('Products.Master.View', '主型号查看', 'Products', 63),
    ('Products.Neutral.Create', '中性产品创建', 'Products', 64),
    ('Products.Neutral.Delete', '中性产品删除', 'Products', 65),
    ('Products.Neutral.Edit', '中性产品编辑', 'Products', 66),
    ('Products.Neutral.View', '中性产品查看', 'Products', 67),
    ('Products.Product.Create', '产品创建', 'Products', 68),
    ('Products.Product.Delete', '产品删除', 'Products', 69),
    ('Products.Product.Edit', '产品编辑', 'Products', 70),
    ('Products.Product.View', '产品查看', 'Products', 71),
    ('QualityControl.AfterSales.CancelConfirmation', '售后单取消确认', 'QualityControl', 72),
    ('QualityControl.AfterSales.Confirm', '售后单确认', 'QualityControl', 73),
    ('QualityControl.AfterSales.Lifecycle', '售后单生命周期', 'QualityControl', 74),
    ('QualityControl.AfterSales.Maintain', '售后单草稿维护', 'QualityControl', 75),
    ('QualityControl.AfterSales.View', '售后单查看', 'QualityControl', 76),
    ('QualityControl.Flow.View', '流量测试组查看', 'QualityControl', 77),
    ('QualityControl.Flow.Create', '流量测试组新增', 'QualityControl', 78),
    ('QualityControl.Flow.Edit', '流量测试组修改', 'QualityControl', 79),
    ('QualityControl.Flow.Delete', '流量测试组删除', 'QualityControl', 80),
    ('Sales.Order.View', '销售订单查看', 'Sales', 81),
    ('Shipping.PackingList.Create', '装箱单创建', 'Shipping', 82),
    ('Shipping.PackingList.Delete', '装箱单删除', 'Shipping', 83),
    ('Shipping.PackingList.Edit', '装箱单编辑', 'Shipping', 84),
    ('Shipping.PackingList.Post', '装箱单过账', 'Shipping', 85),
    ('Shipping.PackingList.Reverse', '装箱单反向', 'Shipping', 86),
    ('Shipping.PackingList.View', '装箱单查看', 'Shipping', 87),
    ('Suppliers.Products.Create', '供应商产品创建', 'Suppliers', 88),
    ('Suppliers.Products.Delete', '供应商产品删除', 'Suppliers', 89),
    ('Suppliers.Products.Edit', '供应商产品编辑', 'Suppliers', 90),
    ('Suppliers.Products.View', '供应商产品查看', 'Suppliers', 91),
    ('Suppliers.Supplier.Create', '供应商创建', 'Suppliers', 92),
    ('Suppliers.Supplier.Delete', '供应商删除', 'Suppliers', 93),
    ('Suppliers.Supplier.Edit', '供应商编辑', 'Suppliers', 94),
    ('Suppliers.Supplier.View', '供应商查看', 'Suppliers', 95),
    ('System.Currency.Create', '币种创建', 'System', 96),
    ('System.Currency.Delete', '币种删除', 'System', 97),
    ('System.Currency.Edit', '币种编辑', 'System', 98),
    ('System.Currency.Manage', '币种管理', 'System', 99),
    ('System.Currency.View', '币种查看', 'System', 100),
    ('System.Dictionary.Create', '字典创建', 'System', 101),
    ('System.Dictionary.Delete', '字典删除', 'System', 102),
    ('System.Dictionary.Edit', '字典编辑', 'System', 103),
    ('System.Dictionary.View', '字典查看', 'System', 104),
    ('System.ExcelFieldMapping.Manage', 'Excel读取配置维护', 'System', 105),
    ('System.ExchangeRate.Create', '汇率创建', 'System', 106),
    ('System.ExchangeRate.Delete', '汇率删除', 'System', 107),
    ('System.ExchangeRate.Edit', '汇率编辑', 'System', 108),
    ('System.ExchangeRate.View', '汇率查看', 'System', 109),
    ('System.Files.Manage', '文件管理', 'System', 110),
    ('System.Numbering.Issue', '单号取号', 'System', 111),
    ('System.Permissions.View', '权限定义查看', 'System', 112),
    ('System.Roles.Manage', '角色管理', 'System', 113),
    ('System.Roles.View', '角色查看', 'System', 114),
    ('System.Users.Manage', '用户管理', 'System', 115),
    ('System.Users.View', '用户查看', 'System', 116);

DO $permission_catalog_sync$
DECLARE
    inserted_count integer;
    updated_count integer;
    external_count integer;
BEGIN
    -- 只验证既有结构，不隐式建表、补列或修改持久化约束。
    IF (SELECT count(*) FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'sys_permission'
        AND column_name IN ('uid', 'version', 'is_deleted', 'created_date', 'created_by', 'updated_date', 'updated_by', 'permission_key', 'display_name', 'group_key', 'sort_order', 'is_active')) <> 12
        OR to_regclass('"public"."sys_role_permission"') IS NULL
        OR NOT EXISTS (SELECT 1 FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'sys_role_permission'
            AND column_name = 'permission_id') THEN
        RAISE EXCEPTION '目标库缺少当前权限定义或角色权限关系结构；目录同步不迁移结构。';
    END IF;

    -- 包括停用、删除与目录外定义；不能合并大小写身份或借同步复活已有定义。
    IF EXISTS (SELECT 1 FROM "public"."sys_permission"
        GROUP BY lower("permission_key" COLLATE "C") HAVING count(*) > 1) THEN
        RAISE EXCEPTION '数据库权限定义存在重复键（含大小写）；请显式处理，同步不会合并。';
    END IF;
    IF EXISTS (SELECT 1 FROM "public"."sys_permission" AS p
        JOIN pg_temp.swift_export_permission_catalog AS c
            ON lower(p."permission_key" COLLATE "C") = lower(c.permission_key)
        WHERE p."permission_key" COLLATE "C" <> c.permission_key) THEN
        RAISE EXCEPTION '数据库权限键大小写与代码不一致；请显式迁移，同步不会改写权限键。';
    END IF;

    -- 既有定义仅变更展示三列：保留 Uid、Key、状态、版本、审计及全部关系。
    UPDATE "public"."sys_permission" AS p
    SET "display_name" = c.display_name, "group_key" = c.group_key, "sort_order" = c.sort_order
    FROM pg_temp.swift_export_permission_catalog AS c
    WHERE p."permission_key" COLLATE "C" = c.permission_key
        AND (p."display_name" COLLATE "C" IS DISTINCT FROM c.display_name
            OR p."group_key" COLLATE "C" IS DISTINCT FROM c.group_key
            OR p."sort_order" IS DISTINCT FROM c.sort_order);
    GET DIAGNOSTICS updated_count = ROW_COUNT;

    -- 缺失键只建定义；随机身份在实际插入时生成，重复执行不改既有身份。
    INSERT INTO "public"."sys_permission" ("uid", "version", "is_deleted", "created_date", "created_by", "updated_date", "updated_by", "permission_key", "display_name", "group_key", "sort_order", "is_active")
    SELECT gen_random_uuid(), 1, FALSE,
        clock_timestamp() AT TIME ZONE 'UTC', 'DbSeed', clock_timestamp() AT TIME ZONE 'UTC', 'DbSeed',
        c.permission_key, c.display_name, c.group_key, c.sort_order, TRUE
    FROM pg_temp.swift_export_permission_catalog AS c
    WHERE NOT EXISTS (SELECT 1 FROM "public"."sys_permission" AS p WHERE p."permission_key" COLLATE "C" = c.permission_key);
    GET DIAGNOSTICS inserted_count = ROW_COUNT;

    SELECT count(*) INTO external_count FROM "public"."sys_permission" AS p
    WHERE NOT EXISTS (SELECT 1 FROM pg_temp.swift_export_permission_catalog AS c WHERE p."permission_key" COLLATE "C" = c.permission_key);
    RAISE NOTICE '权限目录同步完成：新增 %，更新 %；目录外 % 项保留。', inserted_count, updated_count, external_count;
END;
$permission_catalog_sync$;
COMMIT;
