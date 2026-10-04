-- 本批用户授权：测试库可修改，验证后的结构同步 dv_next；保留所有备份及原有数据。
-- 仅将用户名、仓库码和客户/供应商组合唯一键改为已有 ICU + ASCII 尾空格规则。
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
DO $preflight$
DECLARE expected record;
BEGIN
    FOR expected IN SELECT * FROM (VALUES
        ('customer_po_config', 'ux_customer_po_config_codes', 'CREATE UNIQUE INDEX ux_customer_po_config_codes ON public.customer_po_config USING btree (customer_code, supplier_code) WHERE (is_deleted = false)'),
        ('sys_user', 'ux_sys_user_user_name', 'CREATE UNIQUE INDEX ux_sys_user_user_name ON public.sys_user USING btree (user_name) WHERE (is_deleted = false)'),
        ('warehouse_site', 'ux_warehouse_site_code', 'CREATE UNIQUE INDEX ux_warehouse_site_code ON public.warehouse_site USING btree (code) WHERE (is_deleted = false)')
    ) AS definitions(table_name,index_name,index_definition)
    LOOP
        IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_index i JOIN pg_catalog.pg_class x ON x.oid=i.indexrelid
            WHERE i.indexrelid=to_regclass('public.'||expected.index_name)
            AND i.indrelid=to_regclass('public.'||expected.table_name)
            AND i.indisunique AND i.indisvalid AND i.indisready AND i.indislive
            AND NOT i.indisprimary AND NOT i.indisexclusion AND NOT i.indisreplident
            AND NOT i.indisclustered AND i.indexprs IS NULL
            AND x.reltablespace=0 AND x.reloptions IS NULL AND obj_description(x.oid,'pg_class') IS NULL
            AND pg_get_indexdef(i.indexrelid)=expected.index_definition
            AND NOT EXISTS(SELECT 1 FROM pg_catalog.pg_constraint c WHERE c.conindid=i.indexrelid)) THEN
            RAISE EXCEPTION '索引 % 原定义变化，拒绝覆盖',expected.index_name;
        END IF;
        IF to_regclass('public.'||expected.index_name||'_ci_candidate') IS NOT NULL THEN
            RAISE EXCEPTION '候选索引名已被占用';
        END IF;
    END LOOP;
    IF EXISTS (SELECT 1 FROM public.customer_po_config WHERE is_deleted=FALSE GROUP BY rtrim(customer_code,' ') COLLATE public.erp_dictionary_ci, rtrim(supplier_code,' ') COLLATE public.erp_dictionary_ci HAVING count(*)>1) THEN
        RAISE EXCEPTION 'customer_po_config 按源库比较存在重复，不清理记录';
    END IF;
    IF EXISTS (SELECT 1 FROM public.sys_user WHERE is_deleted=FALSE GROUP BY rtrim(user_name,' ') COLLATE public.erp_dictionary_ci HAVING count(*)>1) THEN
        RAISE EXCEPTION 'sys_user 按源库比较存在重复，不清理记录';
    END IF;
    IF EXISTS (SELECT 1 FROM public.warehouse_site WHERE is_deleted=FALSE GROUP BY rtrim(code,' ') COLLATE public.erp_dictionary_ci HAVING count(*)>1) THEN
        RAISE EXCEPTION 'warehouse_site 按源库比较存在重复，不清理记录';
    END IF;
END $preflight$;
-- SQL Server 组合唯一键把 NULL 当作值；仅此可空编码组合使用 NULLS NOT DISTINCT。
CREATE UNIQUE INDEX ux_customer_po_config_codes_ci_candidate ON public.customer_po_config ((rtrim(customer_code, ' ') COLLATE public.erp_dictionary_ci), (rtrim(supplier_code, ' ') COLLATE public.erp_dictionary_ci)) NULLS NOT DISTINCT WHERE is_deleted=FALSE;
CREATE UNIQUE INDEX ux_sys_user_user_name_ci_candidate ON public.sys_user ((rtrim(user_name, ' ') COLLATE public.erp_dictionary_ci)) WHERE is_deleted=FALSE;
CREATE UNIQUE INDEX ux_warehouse_site_code_ci_candidate ON public.warehouse_site ((rtrim(code, ' ') COLLATE public.erp_dictionary_ci)) WHERE is_deleted=FALSE;
DROP INDEX public.ux_customer_po_config_codes;
ALTER INDEX public.ux_customer_po_config_codes_ci_candidate RENAME TO ux_customer_po_config_codes;
DROP INDEX public.ux_sys_user_user_name;
ALTER INDEX public.ux_sys_user_user_name_ci_candidate RENAME TO ux_sys_user_user_name;
DROP INDEX public.ux_warehouse_site_code;
ALTER INDEX public.ux_warehouse_site_code_ci_candidate RENAME TO ux_warehouse_site_code;
COMMIT;
