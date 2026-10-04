-- 已批准范围内的回退脚本：仅恢复两个原始索引，不还原数据。
-- 目标仅 192.168.3.12:5432 的专用测试库与 dv_next；旧备份不在修改范围。
-- 两个规范表达式来自 2026-10-04 专用库前向回滚演练，固定 search_path=pg_catalog。
-- 不使用演练 OID 或模糊字符串匹配推断当前索引语义。
-- 回退同样短时阻断两表读写：等锁 5 秒、单语句 30 秒、事务 60 秒；失败停止并 ROLLBACK。
BEGIN ISOLATION LEVEL READ COMMITTED;
SET LOCAL search_path = pg_catalog;
SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '30s';
SET LOCAL transaction_timeout = '60s';

DO $guard$
BEGIN
    IF current_database() NOT IN ('dv_next_pg_failure_tests_20261004', 'dv_next')
       OR inet_server_addr() IS DISTINCT FROM inet '192.168.3.12'
       OR inet_server_port() IS DISTINCT FROM 5432
       OR current_setting('server_version_num')::integer NOT BETWEEN 180000 AND 189999 THEN
        RAISE EXCEPTION '目标必须是获批的 PostgreSQL 18 库';
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM pg_catalog.pg_collation
        WHERE oid = to_regcollation('public.erp_dictionary_ci')
          AND collprovider = 'i' AND NOT collisdeterministic
          AND colllocale = 'und-u-ks-level2' AND collicurules IS NULL AND collencoding = -1
          AND collversion = '153.128' AND pg_collation_actual_version(oid) = collversion
    ) THEN
        RAISE EXCEPTION 'ICU 配置或版本变化，拒绝回退';
    END IF;
END
$guard$;
LOCK TABLE ONLY public.customer IN ACCESS EXCLUSIVE MODE;
LOCK TABLE ONLY public.supplier IN ACCESS EXCLUSIVE MODE;

DO $preflight$
DECLARE
    expected record;
BEGIN
    FOR expected IN SELECT * FROM (VALUES
        ('customer', 'ux_customer_customer_code', 'rtrim((customer_code)::text, '' ''::text)'),
        ('supplier', 'ux_supplier_supplier_code', 'rtrim((supplier_code)::text, '' ''::text)')
    ) AS definitions(table_name, index_name, expression)
    LOOP
        IF NOT EXISTS (
            SELECT 1 FROM pg_catalog.pg_index i
            JOIN pg_catalog.pg_class x ON x.oid = i.indexrelid
            JOIN pg_catalog.pg_class t ON t.oid = i.indrelid
            JOIN pg_catalog.pg_am am ON am.oid = x.relam
            JOIN pg_catalog.pg_opclass op ON op.oid = i.indclass[0]
            JOIN pg_catalog.pg_namespace ns ON ns.oid = op.opcnamespace
            WHERE i.indexrelid = to_regclass('public.' || expected.index_name)
              AND i.indrelid = to_regclass('public.' || expected.table_name)
              AND i.indisunique AND i.indisvalid AND i.indisready
              AND i.indislive AND i.indimmediate AND NOT i.indisreplident
              AND NOT i.indisclustered AND NOT i.indnullsnotdistinct
              AND NOT i.indisprimary AND NOT i.indisexclusion
              AND i.indnkeyatts = 1 AND i.indnatts = 1 AND i.indkey[0] = 0
              AND i.indoption[0] = 0 AND am.amname = 'btree'
              AND ns.nspname = 'pg_catalog' AND op.opcname = 'text_ops'
              AND t.relkind = 'r' AND x.relkind = 'i'
              AND x.reltablespace = 0 AND x.reloptions IS NULL AND obj_description(x.oid, 'pg_class') IS NULL
              AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_inherits h
                              WHERE h.inhrelid IN (x.oid, t.oid) OR h.inhparent IN (x.oid, t.oid))
              AND i.indcollation[0] = to_regcollation('public.erp_dictionary_ci')
              AND pg_get_expr(i.indexprs, i.indrelid, FALSE) = expected.expression
              AND pg_get_expr(i.indpred, i.indrelid, FALSE) = '(is_deleted = false)'
              AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_constraint c WHERE c.conindid = i.indexrelid)
        ) THEN
            RAISE EXCEPTION '索引 % 不再是本批预期的表达式索引，拒绝回退', expected.index_name;
        END IF;
        IF to_regclass('public.' || expected.index_name || '_rollback_candidate') IS NOT NULL THEN
            RAISE EXCEPTION '回退临时名称已被占用，拒绝覆盖';
        END IF;
    END LOOP;
    -- 虽然正常情况下新规则更严格，仍核验原规则，不假定一定能重建。
    IF EXISTS (SELECT 1 FROM public.customer WHERE is_deleted = FALSE GROUP BY customer_code HAVING count(*) > 1)
       OR EXISTS (SELECT 1 FROM public.supplier WHERE is_deleted = FALSE GROUP BY supplier_code HAVING count(*) > 1) THEN
        RAISE EXCEPTION '原索引规则存在活动编码冲突，拒绝回退，不清理数据';
    END IF;
END
$preflight$;

-- 先改名保留当前防线，再用已保存的真实 indexdef 建原索引，成功后才去掉临时防线。
ALTER INDEX public.ux_customer_customer_code RENAME TO ux_customer_customer_code_rollback_candidate;
ALTER INDEX public.ux_supplier_supplier_code RENAME TO ux_supplier_supplier_code_rollback_candidate;
CREATE UNIQUE INDEX ux_customer_customer_code ON public.customer USING btree (customer_code) WHERE (is_deleted = false);
CREATE UNIQUE INDEX ux_supplier_supplier_code ON public.supplier USING btree (supplier_code) WHERE (is_deleted = false);
DROP INDEX public.ux_customer_customer_code_rollback_candidate;
DROP INDEX public.ux_supplier_supplier_code_rollback_candidate;
COMMIT;
