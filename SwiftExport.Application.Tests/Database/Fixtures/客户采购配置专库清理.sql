-- 仅在主控授权后清理本轮全新专库；不自动删库，不终止其他连接，不使用 CASCADE。
-- 只移除四张夹具表及本轮排序规则；任何额外表或夹具标记不符均停止，不能用于 Legacy 库。
BEGIN;
SET LOCAL lock_timeout = '15s';
DO $$
BEGIN
    IF current_database()<>'dv_customer_po_config_tests_20261018'
       OR (SELECT shobj_description(oid,'pg_database') FROM pg_database WHERE datname=current_database())
            IS DISTINCT FROM 'SwiftExport.CustomerPoConfig.20261018.FreshFixture' THEN
        RAISE EXCEPTION '专库身份或本轮初始化标记不符，拒绝清理';
    END IF;
    IF NULLIF(current_setting('swiftexport.expected_server',TRUE),'') IS NULL
       OR NULLIF(current_setting('swiftexport.expected_port',TRUE),'') IS NULL
       OR inet_server_addr() IS DISTINCT FROM current_setting('swiftexport.expected_server',TRUE)::inet
       OR current_setting('port') IS DISTINCT FROM current_setting('swiftexport.expected_port',TRUE) THEN
        RAISE EXCEPTION '目标实例或端口未确认，拒绝清理';
    END IF;
    IF EXISTS(SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
        WHERE n.nspname NOT IN ('pg_catalog','information_schema') AND n.nspname NOT LIKE 'pg_toast%'
          AND c.relkind IN ('r','p','v','m','S','f')
          AND NOT (n.nspname='public' AND c.relname IN ('customer','supplier','customer_po_config','sys_dic_currency') AND c.relkind='r')) THEN
        RAISE EXCEPTION '发现本轮四表以外的对象，拒绝清理';
    END IF;
END $$;
DROP TABLE public.customer_po_config, public.sys_dic_currency, public.customer, public.supplier RESTRICT;
DROP COLLATION public.erp_dictionary_ci RESTRICT;
COMMENT ON DATABASE dv_customer_po_config_tests_20261018 IS NULL;
COMMIT;
