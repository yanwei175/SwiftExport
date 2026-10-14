-- 物料主型号、中性产品是两个独立的可空身份引用；不建立外键、不反推主型号、不同步颜色。
-- 用户已确认旧 MasterSku/LinkSku 可舍弃。本脚本仅变更结构，不按旧编码匹配、回填或清理数据。
-- 执行前备份并停止旧版本客户端及后端写入；由用户手动执行，不自动部署。
-- 独立会话一次执行全文；前次事务失败时先单独执行 ROLLBACK。
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
        RAISE EXCEPTION '目标实例或库名不符，拒绝物料来源关系迁移：库=%，IP=%，端口=%',
            current_database(), inet_server_addr(), current_setting('port');
    END IF;
    IF to_regclass('public.product') IS NULL THEN
        RAISE EXCEPTION '物料表不存在，拒绝迁移';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public'
        AND table_name='product' AND column_name='product_master_id' AND data_type='uuid' AND is_nullable='YES')
       OR (SELECT count(*) FROM information_schema.columns WHERE table_schema='public'
        AND table_name='product' AND column_name IN ('master_sku','link_sku')
        AND data_type='character varying' AND is_nullable='YES') <> 2
       OR EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public'
        AND table_name='product' AND column_name='product_neutral_id') THEN
        RAISE EXCEPTION '物料来源旧结构不符或新字段已存在，请核对迁移状态';
    END IF;
    IF to_regclass('public.ix_product_product_neutral_id') IS NOT NULL THEN
        RAISE EXCEPTION '中性产品来源索引已存在，拒绝覆盖未知结构';
    END IF;
END $$;

-- 主型号身份及既有主型号/颜色索引原样保留；两个旧编码不参与任何身份转换。
ALTER TABLE public.product DROP COLUMN master_sku;
ALTER TABLE public.product DROP COLUMN link_sku;
ALTER TABLE public.product ADD COLUMN product_neutral_id uuid NULL;
CREATE INDEX ix_product_product_neutral_id ON public.product(product_neutral_id)
    WHERE is_deleted=FALSE AND product_neutral_id IS NOT NULL;
COMMENT ON COLUMN public.product.product_master_id IS '独立引用的主型号身份；允许为空或失效，不从中性产品推导。';
COMMENT ON COLUMN public.product.product_neutral_id IS '独立引用的中性产品身份；允许为空或失效，不同步主型号及颜色。';
COMMIT;
