-- 2026-10-04 第十一批限定范围已获用户批准；回退目录值来自专用库前向回滚演练，不自动应用，不还原数据。
-- 不沿用其他批次已批准状态；仅允许192.168.3.12:5432的专用保留库与dv_next，每次绑定单个approved_database。
-- execution_confirmed默认FALSE；本批审批、原目录核验和新目录实测全部齐备后才可开启。
-- 新pg_get_indexdef、pg_get_expr及谓词已从专用库实测填入；仍逐次核验精确目录，不猜测规范字符串。
-- 已固定search_path=pg_catalog取得真实目录并完成前向/回退ROLLBACK演练；两库现为新索引，后续回退仍须逐次核实目标与授权。
-- 仅恢复两个原列唯一索引，谓词均is_deleted=false；不改主型号颜色组合索引、列、数据、生成器或DI。
-- 保留现有索引防线至两个原索引全部建成；精确目录、ICU及冲突守卫不接受模糊匹配或未知依赖。
-- 固定ONLY主型号→中性最终表排他锁，等锁5秒、单语句30秒、事务60秒；失败停止并整事务ROLLBACK。
-- 不使用CASCADE、不清理任何冲突数据、不跳过失败；提交回应不明时只读核验状态，禁止盲目重跑。
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
LOCK TABLE ONLY public.product_master IN ACCESS EXCLUSIVE MODE;
LOCK TABLE ONLY public.product_neutral IN ACCESS EXCLUSIVE MODE;

DO $preflight$
DECLARE
    expected record;
BEGIN
    -- 列的可空性和类型决定冲突预检含义；结构偏离时停止，不推定原脚本仍适用。
    IF EXISTS (
        SELECT 1 FROM (VALUES
            ('product_master','master_sku','character varying(300)'),
            ('product_neutral','product_code','character varying(300)'),
            ('product_master','is_deleted','boolean'),
            ('product_neutral','is_deleted','boolean')
        ) AS column_spec(table_name,column_name,type_name)
        WHERE NOT EXISTS (
            SELECT 1 FROM pg_catalog.pg_attribute a
            WHERE a.attrelid=to_regclass('public.' || column_spec.table_name)
              AND a.attname=column_spec.column_name AND a.attnum>0 AND NOT a.attisdropped
              AND a.attnotnull AND format_type(a.atttypid,a.atttypmod)=column_spec.type_name
        )
    ) THEN
        RAISE EXCEPTION '编码或软删列定义偏离已核验基线，拒绝执行';
    END IF;
    FOR expected IN SELECT * FROM (VALUES
        ('product_master', 'ux_product_master_master_sku',
         'CREATE UNIQUE INDEX ux_product_master_master_sku ON public.product_master USING btree (rtrim((master_sku)::text, '' ''::text) COLLATE public.erp_dictionary_ci) WHERE (is_deleted = false)',
         'rtrim((master_sku)::text, '' ''::text)', '(is_deleted = false)'),
        ('product_neutral', 'ux_product_neutral_product_code',
         'CREATE UNIQUE INDEX ux_product_neutral_product_code ON public.product_neutral USING btree (rtrim((product_code)::text, '' ''::text) COLLATE public.erp_dictionary_ci) WHERE (is_deleted = false)',
         'rtrim((product_code)::text, '' ''::text)', '(is_deleted = false)')
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
    IF EXISTS (SELECT 1 FROM public.product_master WHERE is_deleted = FALSE GROUP BY master_sku HAVING count(*) > 1)
       OR EXISTS (SELECT 1 FROM public.product_neutral WHERE is_deleted = FALSE GROUP BY product_code HAVING count(*) > 1) THEN
        RAISE EXCEPTION '原索引规则存在活动编码冲突，拒绝回退，不清理数据';
    END IF;
END
$preflight$;

-- 先改名保留当前防线，再按本批提供的原目录定义建两个原索引；全部成功后才去掉临时防线。
ALTER INDEX public.ux_product_master_master_sku RENAME TO ux_product_master_master_sku_rollback_candidate;
ALTER INDEX public.ux_product_neutral_product_code RENAME TO ux_product_neutral_product_code_rollback_candidate;
CREATE UNIQUE INDEX ux_product_master_master_sku ON public.product_master USING btree (master_sku) WHERE (is_deleted = false);
CREATE UNIQUE INDEX ux_product_neutral_product_code ON public.product_neutral USING btree (product_code) WHERE (is_deleted = false);
DROP INDEX public.ux_product_master_master_sku_rollback_candidate;
DROP INDEX public.ux_product_neutral_product_code_rollback_candidate;
-- 提交前保留实际恢复定义，提交回应不明时以独立只读目录核验决定状态，不盲重跑。
SELECT current_database() AS database_name, c.relname AS index_name,
       pg_get_indexdef(i.indexrelid) AS index_definition
FROM pg_catalog.pg_index i JOIN pg_catalog.pg_class c ON c.oid = i.indexrelid
WHERE i.indexrelid IN ('public.ux_product_master_master_sku'::regclass, 'public.ux_product_neutral_product_code'::regclass)
ORDER BY c.relname;
COMMIT;
