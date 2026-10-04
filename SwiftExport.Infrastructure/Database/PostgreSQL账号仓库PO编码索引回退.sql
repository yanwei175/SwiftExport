-- 本批用户授权：测试库可修改，验证后的结构同步 dv_next；保留所有备份及原有数据。
-- 回退仅恢复本批执行前从 PG 取得的三个索引定义，不修改任何数据。
-- SQL Server 原列 Chinese_PRC_CI_AS；既有 ICU 已获采用，仍不完全仿真可忽略字符。
-- 先专用测试库前向/回退演练与实测，后同步 dv_next；本文件不由宿主自动执行。
BEGIN ISOLATION LEVEL READ COMMITTED;
SET LOCAL search_path = pg_catalog;
SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '30s';
SET LOCAL transaction_timeout = '60s';
DO $guard$
BEGIN
    IF current_database() NOT IN ('dv_next_pg_failure_tests_20261004', 'dv_next')
       OR inet_server_addr() IS DISTINCT FROM inet '192.168.3.12'
       OR inet_server_port() IS DISTINCT FROM 5432 THEN
        RAISE EXCEPTION '数据库或实例不属于本批授权范围';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_collation WHERE oid=to_regcollation('public.erp_dictionary_ci')
        AND collprovider='i' AND NOT collisdeterministic AND colllocale='und-u-ks-level2'
        AND collversion='153.128' AND pg_collation_actual_version(oid)=collversion) THEN
        RAISE EXCEPTION '既有 ICU 规则或版本变化，停止迁移';
    END IF;
END $guard$;
-- 三张表统一锁顺序，避免先检查再升级锁；不改数据，也不使用 CASCADE。
LOCK TABLE ONLY public.customer_po_config IN ACCESS EXCLUSIVE MODE;
LOCK TABLE ONLY public.sys_user IN ACCESS EXCLUSIVE MODE;
LOCK TABLE ONLY public.warehouse_site IN ACCESS EXCLUSIVE MODE;
DO $rollback_guard$
DECLARE expected record;
BEGIN
    FOR expected IN SELECT * FROM (VALUES
        ('ux_customer_po_config_codes', 'CREATE UNIQUE INDEX ux_customer_po_config_codes ON public.customer_po_config USING btree (rtrim((customer_code)::text, '' ''::text) COLLATE public.erp_dictionary_ci, rtrim((supplier_code)::text, '' ''::text) COLLATE public.erp_dictionary_ci) NULLS NOT DISTINCT WHERE (is_deleted = false)'),
        ('ux_sys_user_user_name', 'CREATE UNIQUE INDEX ux_sys_user_user_name ON public.sys_user USING btree (rtrim((user_name)::text, '' ''::text) COLLATE public.erp_dictionary_ci) WHERE (is_deleted = false)'),
        ('ux_warehouse_site_code', 'CREATE UNIQUE INDEX ux_warehouse_site_code ON public.warehouse_site USING btree (rtrim((code)::text, '' ''::text) COLLATE public.erp_dictionary_ci) WHERE (is_deleted = false)')
    ) AS definitions(index_name,index_definition)
    LOOP
        IF NOT EXISTS(SELECT 1 FROM pg_catalog.pg_index i WHERE i.indexrelid=to_regclass('public.'||expected.index_name)
            AND i.indisunique AND i.indisvalid AND i.indisready AND i.indislive
            AND NOT i.indisprimary AND pg_get_indexdef(i.indexrelid)=expected.index_definition
            AND NOT EXISTS(SELECT 1 FROM pg_catalog.pg_constraint c WHERE c.conindid=i.indexrelid)) THEN
            RAISE EXCEPTION '索引 % 已偏离本批交付，拒绝回退', expected.index_name;
        END IF;
        IF to_regclass('public.'||expected.index_name||'_rollback_candidate') IS NOT NULL THEN
            RAISE EXCEPTION '回退候选名称被占用';
        END IF;
    END LOOP;
END $rollback_guard$;
CREATE UNIQUE INDEX ux_customer_po_config_codes_rollback_candidate ON public.customer_po_config (customer_code, supplier_code) WHERE is_deleted=FALSE;
CREATE UNIQUE INDEX ux_sys_user_user_name_rollback_candidate ON public.sys_user (user_name) WHERE is_deleted=FALSE;
CREATE UNIQUE INDEX ux_warehouse_site_code_rollback_candidate ON public.warehouse_site (code) WHERE is_deleted=FALSE;
DROP INDEX public.ux_customer_po_config_codes;
ALTER INDEX public.ux_customer_po_config_codes_rollback_candidate RENAME TO ux_customer_po_config_codes;
DROP INDEX public.ux_sys_user_user_name;
ALTER INDEX public.ux_sys_user_user_name_rollback_candidate RENAME TO ux_sys_user_user_name;
DROP INDEX public.ux_warehouse_site_code;
ALTER INDEX public.ux_warehouse_site_code_rollback_candidate RENAME TO ux_warehouse_site_code;
COMMIT;
