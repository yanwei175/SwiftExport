-- 将空的系列名称字段转换为字典明细身份；不创建字典值，不改主型号业务版本或审计。
-- 生产执行前备份并停止主型号写入；旧 product_series 非空时停止，不自动映射或清理。
-- 使用独立会话一次执行全文；前次事务失败时先单独执行 ROLLBACK。
-- 专用库预演只将 expected_database 改为 dv_next_product_attribute_tests_20261012。
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
        RAISE EXCEPTION '目标实例或库名不符，拒绝系列字典引用迁移：库=%，IP=%，端口=%',
            current_database(), inet_server_addr(), current_setting('port');
    END IF;
    IF to_regclass('public.product_master') IS NULL OR to_regclass('public.sys_dict_data') IS NULL THEN
        RAISE EXCEPTION '主型号表或字典明细表不存在，拒绝迁移';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public'
        AND table_name='product_master' AND column_name='product_series'
        AND data_type='character varying' AND character_maximum_length=500 AND is_nullable='YES')
       OR EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public'
        AND table_name='product_master' AND column_name='product_series_id') THEN
        RAISE EXCEPTION '旧系列字段结构不符或新字段已存在，请核对迁移状态';
    END IF;
    IF to_regclass('public.ix_product_master_series') IS NOT NULL THEN
        RAISE EXCEPTION '新系列索引已存在，拒绝覆盖未知结构';
    END IF;
END $$;

-- 冻结所有主型号记录后重新检查，包括软删除行；不能丢弃任何已有系列内容。
LOCK TABLE public.product_master IN ACCESS EXCLUSIVE MODE;
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM public.product_master WHERE length(btrim(coalesce(product_series,''))) > 0) THEN
        RAISE EXCEPTION '旧产品系列字段仍有非空内容，拒绝迁移；请先核对数据映射方案，不要直接清空';
    END IF;
END $$;

ALTER TABLE public.product_master DROP COLUMN product_series;
ALTER TABLE public.product_master ADD COLUMN product_series_id uuid NULL;
CREATE INDEX ix_product_master_series ON public.product_master(product_series_id)
    WHERE is_deleted=FALSE AND product_series_id IS NOT NULL;
COMMENT ON COLUMN public.product_master.product_series_id IS '产品系列字典明细身份；为空表示未设置，改名保持引用，不参与标签选择。';
COMMIT;
