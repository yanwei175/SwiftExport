-- 修正已执行“PostgreSQL物料来源关系收敛迁移.sql”后的结构：物料 LinkSku 为自身可空型号。
-- 产品型号可以不在中性产品资料中存在；主型号身份继续独立保存，不恢复 master_sku。
-- 本脚本仅恢复正确字段结构，不恢复旧数据、不按中性产品身份回填、不修改其他物料事实。
-- 停止旧版本客户端及后端写入后，由用户手动执行；不自动部署。
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
        RAISE EXCEPTION '目标实例或库名不符，拒绝物料独立型号修正：库=%，IP=%，端口=%',
            current_database(), inet_server_addr(), current_setting('port');
    END IF;
    IF to_regclass('public.product') IS NULL THEN
        RAISE EXCEPTION '物料表不存在，拒绝修正';
    END IF;
END $$;

-- 锁定后检查已执行旧迁移的准确结构，避免对其他阶段或已修正的库重复执行。
LOCK TABLE public.product IN ACCESS EXCLUSIVE MODE;
DO $$
BEGIN
    IF (SELECT count(*) FROM information_schema.columns WHERE table_schema='public'
        AND table_name='product' AND column_name IN ('product_master_id','product_neutral_id')
        AND data_type='uuid' AND is_nullable='YES') <> 2
       OR EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public'
        AND table_name='product' AND column_name IN ('master_sku','link_sku')) THEN
        RAISE EXCEPTION '物料结构不符合已执行来源关系迁移的状态，请核对后再执行';
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM pg_index index_info
        WHERE index_info.indexrelid=to_regclass('public.ix_product_product_neutral_id')
          AND index_info.indrelid='public.product'::regclass
          AND index_info.indisunique=FALSE AND index_info.indnatts=1 AND index_info.indnkeyatts=1
          AND index_info.indkey[0]=(SELECT attnum FROM pg_attribute
              WHERE attrelid='public.product'::regclass AND attname='product_neutral_id' AND attisdropped=FALSE)
    ) THEN
        RAISE EXCEPTION '中性产品身份索引缺失或结构不符，拒绝删除未知索引';
    END IF;
END $$;

-- 只调整当前型号字段；不对自由型号增加唯一约束，同型号可对应多条物料。
DROP INDEX public.ix_product_product_neutral_id;
ALTER TABLE public.product DROP COLUMN product_neutral_id;
ALTER TABLE public.product ADD COLUMN link_sku varchar(300) NULL;
COMMENT ON COLUMN public.product.link_sku IS '物料自身的产品型号；可自由维护，按同名编码反查中性产品，无匹配也不影响业务。';
COMMIT;
