-- 2026-10-04 用户批准：先专用库验收，再同步 dv_next；不是启动脚本，不自动应用。
-- 连接目标仅 192.168.3.12:5432 下方两个明确库名，执行器还须核实端点及执行顺序。
-- 仅替换两个活动编码唯一索引，不修改数据或列；旧备份保留，新备份在验收后另建。
-- 已确认采用 ICU；不完全仿真 Chinese_PRC_CI_AS，尤其 U+200B 等可忽略字符。
-- 普通 DROP INDEX 需要表排他锁，短时阻断这两张表的读写，不断开其他连接。
-- 等锁至多 5 秒，单语句至多 30 秒，事务至多 60 秒；任何失败停止并 ROLLBACK。
-- 非幂等：已执行或结构偏离时拒绝重复执行，不静默覆盖。
-- 获批后先将末尾 COMMIT 替换为 ROLLBACK 演练，取得规范表达式以补齐回退脚本，再正式提交。
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
          AND colllocale = 'und-u-ks-level2'
          AND collicurules IS NULL AND collencoding = -1
          AND collversion = '153.128' AND pg_collation_actual_version(oid) = collversion
    ) THEN
        RAISE EXCEPTION 'ICU 配置或版本与已核验基线不同，拒绝变更';
    END IF;
END
$guard$;

-- 一开始取得最终所需锁，避免先 SHARE 后升级导致等待或死锁；两表固定同一顺序。
LOCK TABLE ONLY public.customer IN ACCESS EXCLUSIVE MODE;
LOCK TABLE ONLY public.supplier IN ACCESS EXCLUSIVE MODE;

DO $preflight$
DECLARE
    expected record;
BEGIN
    -- 此处保存的是 2026-10-04 从该专用库只读取得的真实原索引定义，回退脚本据此恢复。
    FOR expected IN SELECT * FROM (VALUES
        ('customer', 'ux_customer_customer_code',
         'CREATE UNIQUE INDEX ux_customer_customer_code ON public.customer USING btree (customer_code) WHERE (is_deleted = false)'),
        ('supplier', 'ux_supplier_supplier_code',
         'CREATE UNIQUE INDEX ux_supplier_supplier_code ON public.supplier USING btree (supplier_code) WHERE (is_deleted = false)')
    ) AS definitions(table_name, index_name, index_definition)
    LOOP
        IF NOT EXISTS (
            SELECT 1 FROM pg_catalog.pg_index i
            JOIN pg_catalog.pg_class x ON x.oid = i.indexrelid
            JOIN pg_catalog.pg_class t ON t.oid = i.indrelid
            WHERE i.indexrelid = to_regclass('public.' || expected.index_name)
              AND i.indrelid = to_regclass('public.' || expected.table_name)
              AND i.indisunique AND i.indisvalid AND i.indisready
              AND i.indislive AND i.indimmediate AND NOT i.indisreplident
              AND NOT i.indisclustered AND NOT i.indnullsnotdistinct
              AND NOT i.indisprimary AND NOT i.indisexclusion
              AND i.indnkeyatts = 1 AND i.indnatts = 1 AND i.indexprs IS NULL
              AND t.relkind = 'r' AND x.relkind = 'i'
              AND x.reltablespace = 0 AND x.reloptions IS NULL AND obj_description(x.oid, 'pg_class') IS NULL
              AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_inherits h
                              WHERE h.inhrelid IN (x.oid, t.oid) OR h.inhparent IN (x.oid, t.oid))
              AND pg_get_indexdef(i.indexrelid) = expected.index_definition
              AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_constraint c WHERE c.conindid = i.indexrelid)
        ) THEN
            RAISE EXCEPTION '原索引 % 缺失、定义变化或被约束占用，拒绝变更', expected.index_name;
        END IF;
        IF to_regclass('public.' || expected.index_name || '_ci_candidate') IS NOT NULL THEN
            RAISE EXCEPTION '候选索引名称已被占用，拒绝覆盖';
        END IF;
    END LOOP;

    -- 在排他锁内重新检查，不能以先前只读预检代替写入时检查；不输出或清理冲突记录。
    IF EXISTS (SELECT 1 FROM public.customer WHERE is_deleted = FALSE
               GROUP BY rtrim(customer_code, ' ') COLLATE public.erp_dictionary_ci HAVING count(*) > 1)
       OR EXISTS (SELECT 1 FROM public.supplier WHERE is_deleted = FALSE
                  GROUP BY rtrim(supplier_code, ' ') COLLATE public.erp_dictionary_ci HAVING count(*) > 1) THEN
        RAISE EXCEPTION '拟议比较规则存在活动编码冲突，拒绝修改索引';
    END IF;
END
$preflight$;

-- 新索引全部建成后才替换旧索引；不用 CASCADE，任何未知依赖都会使整个事务失败。
CREATE UNIQUE INDEX ux_customer_customer_code_ci_candidate
    ON public.customer ((rtrim(customer_code, ' ') COLLATE public.erp_dictionary_ci)) WHERE is_deleted = FALSE;
CREATE UNIQUE INDEX ux_supplier_supplier_code_ci_candidate
    ON public.supplier ((rtrim(supplier_code, ' ') COLLATE public.erp_dictionary_ci)) WHERE is_deleted = FALSE;
DROP INDEX public.ux_customer_customer_code;
DROP INDEX public.ux_supplier_supplier_code;
ALTER INDEX public.ux_customer_customer_code_ci_candidate RENAME TO ux_customer_customer_code;
ALTER INDEX public.ux_supplier_supplier_code_ci_candidate RENAME TO ux_supplier_supplier_code;

-- 演练及正式提交都保存此结果；精确表达式用于回退守卫，不猜测服务端规范化格式。
SELECT current_database() AS database_name, c.relname AS index_name,
       pg_get_indexdef(i.indexrelid) AS index_definition,
       pg_get_expr(i.indexprs, i.indrelid, FALSE) AS expression,
       pg_get_expr(i.indpred, i.indrelid, FALSE) AS predicate
FROM pg_catalog.pg_index i JOIN pg_catalog.pg_class c ON c.oid = i.indexrelid
WHERE i.indexrelid IN ('public.ux_customer_customer_code'::regclass, 'public.ux_supplier_supplier_code'::regclass)
ORDER BY c.relname;
COMMIT;
