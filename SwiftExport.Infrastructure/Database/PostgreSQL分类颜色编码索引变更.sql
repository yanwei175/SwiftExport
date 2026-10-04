-- 2026-10-04 用户已批准本批限定范围；不是启动脚本，不自动应用。
-- 仅允许 192.168.3.12:5432 的 dv_next_pg_failure_tests_20261004、dv_next；获批后先专用库演练验收，再同步 dv_next。
-- execution_confirmed 默认 FALSE；开启前必须取得本批明确审批并核实精确目录状态，不得沿用上一批审批。
-- 主控已核验两库原定义一致、拟议冲突组为零；专用库前向回滚演练已取得回退目录事实。
-- 仅规范化索引键的 ASCII 尾空格并使用既有 ICU，不改列/数据/生成器/基线，不补 NULL 分类码。
-- 分类保持 is_deleted=false AND category_code IS NOT NULL；颜色保持 is_deleted=false，不附加启停过滤。
-- ICU 基线 153.128 来自上一批，执行时仍严格核验；不宣称完整仿真 Chinese_PRC_CI_AS（例如 U+200B）。
-- 普通 DROP INDEX 必须短时取得最终表排他锁；固定 ONLY 分类→颜色顺序，阻断两表读写但不断开其他连接。
-- 等锁至多5秒、单语句30秒、事务60秒；任何失败停止执行并整事务 ROLLBACK，禁止继续提交。
-- 非幂等：结构偏离、候选名称占用或重复应用均拒绝覆盖；不清理冲突数据，不使用 CASCADE。
-- 获批演练须将末尾 COMMIT 替换为 ROLLBACK，保存目录输出以补齐回退守卫；回退可审查及演练通过后才能正式提交。
BEGIN ISOLATION LEVEL READ COMMITTED;
SET LOCAL search_path = pg_catalog;
SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '30s';
SET LOCAL transaction_timeout = '60s';

DO $guard$
DECLARE
    execution_confirmed boolean := FALSE; -- 仅在本批明确审批及目录核验完成后手工开启。
    approved_database text := '__待指定单个获批目标库__'; -- 每次执行绑定一个阶段目标，禁止仅凭两库白名单跳过专用库验收。
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
          AND colllocale = 'und-u-ks-level2'
          AND collicurules IS NULL AND collencoding = -1
          AND collversion = '153.128' AND pg_collation_actual_version(oid) = collversion
    ) THEN
        RAISE EXCEPTION 'ICU 配置或版本与已核验基线不同，拒绝变更';
    END IF;
END
$guard$;

-- 一开始取得最终所需锁，避免先 SHARE 后升级导致等待或死锁；两表固定同一顺序。
LOCK TABLE ONLY public.product_category IN ACCESS EXCLUSIVE MODE;
LOCK TABLE ONLY public.product_color IN ACCESS EXCLUSIVE MODE;

DO $preflight$
DECLARE
    expected record;
BEGIN
    -- 原定义来自主控 2026-10-04 对两个目标库的只读实查；执行时仍逐字核验，偏差立即拒绝，不推定 DDL 已验收。
    FOR expected IN SELECT * FROM (VALUES
        ('product_category', 'ux_product_category_category_code',
         'CREATE UNIQUE INDEX ux_product_category_category_code ON public.product_category USING btree (category_code) WHERE ((is_deleted = false) AND (category_code IS NOT NULL))'),
        ('product_color', 'ux_product_color_color_code_unique',
         'CREATE UNIQUE INDEX ux_product_color_color_code_unique ON public.product_color USING btree (color_code_unique) WHERE (is_deleted = false)')
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
    IF EXISTS (SELECT 1 FROM public.product_category WHERE is_deleted = FALSE AND category_code IS NOT NULL
               GROUP BY rtrim(category_code, ' ') COLLATE public.erp_dictionary_ci HAVING count(*) > 1)
       OR EXISTS (SELECT 1 FROM public.product_color WHERE is_deleted = FALSE
                  GROUP BY rtrim(color_code_unique, ' ') COLLATE public.erp_dictionary_ci HAVING count(*) > 1) THEN
        RAISE EXCEPTION '拟议比较规则存在活动编码冲突，拒绝修改索引';
    END IF;
END
$preflight$;

-- 新索引全部建成后才替换旧索引；不用 CASCADE，任何未知依赖都会使整个事务失败。
CREATE UNIQUE INDEX ux_product_category_category_code_ci_candidate
    ON public.product_category ((rtrim(category_code, ' ') COLLATE public.erp_dictionary_ci)) WHERE is_deleted = FALSE AND category_code IS NOT NULL;
CREATE UNIQUE INDEX ux_product_color_color_code_unique_ci_candidate
    ON public.product_color ((rtrim(color_code_unique, ' ') COLLATE public.erp_dictionary_ci)) WHERE is_deleted = FALSE;
DROP INDEX public.ux_product_category_category_code;
DROP INDEX public.ux_product_color_color_code_unique;
ALTER INDEX public.ux_product_category_category_code_ci_candidate RENAME TO ux_product_category_category_code;
ALTER INDEX public.ux_product_color_color_code_unique_ci_candidate RENAME TO ux_product_color_color_code_unique;

-- 演练及正式提交都保存此结果；精确表达式用于回退守卫，不猜测服务端规范化格式。
SELECT current_database() AS database_name, c.relname AS index_name,
       pg_get_indexdef(i.indexrelid) AS index_definition,
       pg_get_expr(i.indexprs, i.indrelid, FALSE) AS expression,
       pg_get_expr(i.indpred, i.indrelid, FALSE) AS predicate
FROM pg_catalog.pg_index i JOIN pg_catalog.pg_class c ON c.oid = i.indexrelid
WHERE i.indexrelid IN ('public.ux_product_category_category_code'::regclass, 'public.ux_product_color_color_code_unique'::regclass)
ORDER BY c.relname;
COMMIT;
