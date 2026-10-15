-- 产品包装第一阶段：装箱参数只采用有效 Carton，移除 product_package.is_default。
-- 2026-10-15 只读核对 192.168.3.12:5432/dv_next：物料身份列及唯一索引已存在，包装表当时为 0 行。
-- 本文件仅为人工增量升级交付，不由应用或生成器自动执行；执行时重新检查结构，不依赖上述行数。
-- 升级顺序：先完成“PostgreSQL产品包装物料身份迁移.sql”（仅对符合其前置条件的旧库），
-- 再停止旧后端、经授权执行本文件，最后启用本阶段代码。已完成身份迁移的库不要重跑旧脚本。
-- 本脚本支持已有包装数据，不清表、不改包材引用或包装类型，不为已有数据猜测外箱。
-- 若尚无有效 Carton，新代码会返回未设置；旧默认标记不再作为选择依据。
BEGIN;
SELECT set_config('swiftexport.expected_database', 'dv_next', true);
SELECT set_config('swiftexport.expected_server', '192.168.3.12', true);
SELECT set_config('swiftexport.expected_port', '5432', true);
SET LOCAL lock_timeout = '15s';
SET LOCAL statement_timeout = '60s';

DO $$
BEGIN
    IF current_database() <> current_setting('swiftexport.expected_database')
       OR inet_server_addr() IS DISTINCT FROM current_setting('swiftexport.expected_server')::inet
       OR current_setting('port') <> current_setting('swiftexport.expected_port') THEN
        RAISE EXCEPTION '目标实例或库名不符，拒绝外箱规则迁移：库=%，IP=%，端口=%',
            current_database(), inet_server_addr(), current_setting('port');
    END IF;
    IF to_regclass('public.product_package') IS NULL THEN
        RAISE EXCEPTION '产品包装规格表不存在，拒绝迁移';
    END IF;
END $$;

-- 与结构校验在同一事务持有锁；只删除明确退役列，依赖冲突由 RESTRICT 拒绝，不级联删除其他对象。
LOCK TABLE public.product_package IN ACCESS EXCLUSIVE MODE;
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public'
        AND table_name='product_package' AND column_name IN ('product_id','packaging_material_sku'))
       OR (SELECT count(*) FROM information_schema.columns WHERE table_schema='public'
           AND table_name='product_package' AND column_name IN ('owner_product_id','packaging_material_id')
           AND data_type='uuid' AND is_nullable='YES') <> 2 THEN
        RAISE EXCEPTION '包装物料身份结构不符，请先核对身份迁移；有数据的旧结构须另行确认转换方案';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public'
        AND table_name='product_package' AND column_name='is_default' AND data_type='boolean' AND is_nullable='NO') THEN
        RAISE EXCEPTION '默认包装列缺失或结构不符，可能已迁移，请只读核对后停止重复执行';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public'
        AND table_name='product_package' AND column_name='package_type' AND data_type='character varying' AND is_nullable='NO')
       OR NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public'
        AND table_name='product_package' AND column_name='is_deleted' AND data_type='boolean' AND is_nullable='NO') THEN
        RAISE EXCEPTION '包装类型或软删除结构不符，拒绝迁移';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_index i
        WHERE i.indexrelid=to_regclass('public.ux_product_package_owner_type')
          AND i.indrelid='public.product_package'::regclass AND i.indisunique AND i.indisvalid AND i.indisready
          AND i.indnatts=2 AND i.indnkeyatts=2
          AND pg_get_indexdef(i.indexrelid,1,TRUE)='owner_product_id'
          AND pg_get_indexdef(i.indexrelid,2,TRUE)='package_type'
          AND lower(regexp_replace(pg_get_expr(i.indpred,i.indrelid),'[[:space:]()]','','g'))
              = 'is_deleted=falseandowner_product_idisnotnull') THEN
        RAISE EXCEPTION '所属物料和包装类型的有效记录唯一索引不符，拒绝迁移';
    END IF;
END $$;

ALTER TABLE public.product_package DROP COLUMN is_default RESTRICT;
COMMENT ON COLUMN public.product_package.package_type IS 'ProductPackageType 字典的稳定英文代码；支持内盒、单套运输盒、出货外箱、托盘及自定义类型，各层可省略。';
COMMENT ON COLUMN public.product_package.pcs_per_package IS '装率：该包装内含有的成品基础单位数，不是下级包装数量。';
COMMIT;
