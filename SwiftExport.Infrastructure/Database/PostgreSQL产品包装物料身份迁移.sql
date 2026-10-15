-- 产品包装规格：明确被包装物料 OwnerProductId 和包材物料 PackagingMaterialId，二者均逻辑引用 Product.Uid。
-- 已于 2026-10-15 只读核对目标实例结构及空表状态；执行时仍会在锁内重新检查。
-- 适用于下方确认的 dv_next 空包装表，不转换已有包材编码，不清理数据，不新增物理外键。
-- 停止旧后端使用后，由用户在独立会话一次执行全文；前次事务失败时先单独执行 ROLLBACK。
BEGIN;
SELECT set_config('swiftexport.expected_database', 'dv_next', true);
SELECT set_config('swiftexport.expected_server', '192.168.3.12', true);
SELECT set_config('swiftexport.expected_port', '5432', true);
SET LOCAL lock_timeout = '15s';

DO $$
BEGIN
    IF current_database() <> current_setting('swiftexport.expected_database')
       OR inet_server_addr() IS DISTINCT FROM current_setting('swiftexport.expected_server')::inet
       OR current_setting('port') <> current_setting('swiftexport.expected_port') THEN
        RAISE EXCEPTION '目标实例或库名不符，拒绝产品包装迁移：库=%，IP=%，端口=%',
            current_database(), inet_server_addr(), current_setting('port');
    END IF;
    IF to_regclass('public.product_package') IS NULL THEN
        RAISE EXCEPTION '产品包装规格表不存在，拒绝迁移';
    END IF;
END $$;

-- 锁内核对旧结构和全表为空，包括软删除记录；存在数据时停止，不猜测包材编码对应的物料。
LOCK TABLE public.product_package IN ACCESS EXCLUSIVE MODE;
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public'
        AND table_name='product_package' AND column_name='product_id' AND data_type='uuid' AND is_nullable='YES')
       OR NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public'
        AND table_name='product_package' AND column_name='packaging_material_sku'
        AND data_type='character varying' AND character_maximum_length=300 AND is_nullable='YES')
       OR EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public'
        AND table_name='product_package' AND column_name IN ('owner_product_id','packaging_material_id')) THEN
        RAISE EXCEPTION '包装表不是预期旧结构，或已执行迁移，请核对后再执行';
    END IF;
    IF EXISTS (SELECT 1 FROM public.product_package) THEN
        RAISE EXCEPTION '包装表已有数据，拒绝空表迁移；请先制定现有包材编码转换方案';
    END IF;
    IF to_regclass('public.ux_product_package_owner_type') IS NOT NULL
       OR NOT EXISTS (SELECT 1 FROM pg_index i
        WHERE i.indexrelid=to_regclass('public.ux_product_package_product_type')
          AND i.indrelid='public.product_package'::regclass AND i.indisunique AND i.indisvalid
          AND i.indnatts=2 AND i.indnkeyatts=2
          AND pg_get_indexdef(i.indexrelid,1,TRUE)='product_id'
          AND pg_get_indexdef(i.indexrelid,2,TRUE)='package_type') THEN
        RAISE EXCEPTION '包装归属唯一索引缺失、结构不符或新索引名称已占用，拒绝迁移';
    END IF;
END $$;

-- 原位调整字段，保留表、主键、尺寸重量和约束；列改名会同步更新既有索引的键和过滤表达式。
ALTER TABLE public.product_package RENAME COLUMN product_id TO owner_product_id;
ALTER TABLE public.product_package RENAME COLUMN packaging_material_sku TO packaging_material_id;
ALTER TABLE public.product_package ALTER COLUMN packaging_material_id TYPE uuid USING packaging_material_id::uuid;
ALTER INDEX public.ux_product_package_product_type RENAME TO ux_product_package_owner_type;
COMMENT ON COLUMN public.product_package.owner_product_id IS '这套包装规格所属的被包装物料身份，逻辑引用 Product.Uid。';
COMMENT ON COLUMN public.product_package.packaging_material_id IS '使用的包材物料身份，逻辑引用 Product.Uid；不承担包装规格自身的 Uid。';
COMMIT;
