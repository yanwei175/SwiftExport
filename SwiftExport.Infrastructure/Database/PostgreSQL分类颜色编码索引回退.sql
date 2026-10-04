-- 2026-10-04 用户已批准本批限定范围；回退不自动应用、不还原数据。
-- 仅允许 192.168.3.12:5432 的 dv_next_pg_failure_tests_20261004、dv_next；旧备份不在修改范围。
-- execution_confirmed 默认 FALSE；获得本批明确审批且补齐演练事实后才可开启。
-- 新索引定义、表达式及谓词来自2026-10-04专用库前向回滚演练，固定search_path=pg_catalog。
-- 主控须在 search_path=pg_catalog 的获批前向回滚演练取得真实规范值，不得猜测格式或伪造已验收守卫。
-- 原列重建语句来自主控对两个目标库的只读实查，原定义完全相同；执行前仍逐项核验。
-- 分类保持 NULL 码不参与唯一，不填码；颜色恢复原 color_code_unique 比较；不改列或数据。
-- 固定 ONLY 分类→颜色取最终表排他锁，等锁5秒、单语句30秒、事务60秒；失败停止并整事务 ROLLBACK。
-- 防线保留至两个原索引全部建成，不用 CASCADE 或额外锁；回退后核验原目录定义和整库数据摘要。
BEGIN ISOLATION LEVEL READ COMMITTED;
SET LOCAL search_path = pg_catalog;
SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '30s';
SET LOCAL transaction_timeout = '60s';

DO $guard$
DECLARE
    execution_confirmed boolean := FALSE; -- 仅在本批明确审批及目录核验完成后手工开启。
    approved_database text := '__待指定单个获批目标库__'; -- 回退也必须绑定本次获批的单库，不能由白名单推定目标。
BEGIN
    IF NOT execution_confirmed THEN
        RAISE EXCEPTION '本次执行开关未确认，默认拒绝执行';
    END IF;
    IF current_database() IS DISTINCT FROM approved_database
       OR current_database() NOT IN ('dv_next_pg_failure_tests_20261004', 'dv_next')
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
LOCK TABLE ONLY public.product_category IN ACCESS EXCLUSIVE MODE;
LOCK TABLE ONLY public.product_color IN ACCESS EXCLUSIVE MODE;

DO $preflight$
DECLARE
    expected record;
BEGIN
    FOR expected IN SELECT * FROM (VALUES
        ('product_category', 'ux_product_category_category_code', 'CREATE UNIQUE INDEX ux_product_category_category_code ON public.product_category USING btree (rtrim((category_code)::text, '' ''::text) COLLATE public.erp_dictionary_ci) WHERE ((is_deleted = false) AND (category_code IS NOT NULL))', 'rtrim((category_code)::text, '' ''::text)', '((is_deleted = false) AND (category_code IS NOT NULL))'),
        ('product_color', 'ux_product_color_color_code_unique', 'CREATE UNIQUE INDEX ux_product_color_color_code_unique ON public.product_color USING btree (rtrim((color_code_unique)::text, '' ''::text) COLLATE public.erp_dictionary_ci) WHERE (is_deleted = false)', 'rtrim((color_code_unique)::text, '' ''::text)', '(is_deleted = false)')
    ) AS definitions(table_name, index_name, index_definition, expression, predicate)
    LOOP
        IF position('__未填_' in expected.index_definition) > 0
           OR position('__未填_' in expected.expression) > 0
           OR position('__未填_' in expected.predicate) > 0 THEN
            RAISE EXCEPTION '新索引演练目录事实尚未填入，拒绝回退';
        END IF;
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
              AND pg_get_expr(i.indpred, i.indrelid, FALSE) = expected.predicate
              AND pg_get_indexdef(i.indexrelid) = expected.index_definition
              AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_constraint c WHERE c.conindid = i.indexrelid)
        ) THEN
            RAISE EXCEPTION '索引 % 不再是本批预期的表达式索引，拒绝回退', expected.index_name;
        END IF;
        IF to_regclass('public.' || expected.index_name || '_rollback_candidate') IS NOT NULL THEN
            RAISE EXCEPTION '回退临时名称已被占用，拒绝覆盖';
        END IF;
    END LOOP;
    -- 虽然正常情况下新规则更严格，仍核验原规则，不假定一定能重建。
    IF EXISTS (SELECT 1 FROM public.product_category WHERE is_deleted = FALSE AND category_code IS NOT NULL GROUP BY category_code HAVING count(*) > 1)
       OR EXISTS (SELECT 1 FROM public.product_color WHERE is_deleted = FALSE GROUP BY color_code_unique HAVING count(*) > 1) THEN
        RAISE EXCEPTION '原索引规则存在活动编码冲突，拒绝回退，不清理数据';
    END IF;
END
$preflight$;

-- 先改名保留当前防线，再用已保存的真实 indexdef 建原索引，成功后才去掉临时防线。
ALTER INDEX public.ux_product_category_category_code RENAME TO ux_product_category_category_code_rollback_candidate;
ALTER INDEX public.ux_product_color_color_code_unique RENAME TO ux_product_color_color_code_unique_rollback_candidate;
CREATE UNIQUE INDEX ux_product_category_category_code ON public.product_category USING btree (category_code) WHERE ((is_deleted = false) AND (category_code IS NOT NULL));
CREATE UNIQUE INDEX ux_product_color_color_code_unique ON public.product_color USING btree (color_code_unique) WHERE (is_deleted = false);
DROP INDEX public.ux_product_category_category_code_rollback_candidate;
DROP INDEX public.ux_product_color_color_code_unique_rollback_candidate;
-- 提交前保留实际恢复定义，提交回应不明时以独立只读目录核验决定状态，不盲重跑。
SELECT current_database() AS database_name, c.relname AS index_name,
       pg_get_indexdef(i.indexrelid) AS index_definition
FROM pg_catalog.pg_index i JOIN pg_catalog.pg_class c ON c.oid = i.indexrelid
WHERE i.indexrelid IN ('public.ux_product_category_category_code'::regclass, 'public.ux_product_color_color_code_unique'::regclass)
ORDER BY c.relname;
COMMIT;
