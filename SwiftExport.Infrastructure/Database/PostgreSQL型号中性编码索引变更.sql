-- 2026-10-04 用户已批准第十一批型号/中性编码索引限定变更，不是启动脚本，不自动应用。
-- 本批独立获批；不能把此范围扩展到其他索引、数据库或业务规则。
-- 目标仅 192.168.3.12:5432 的 dv_next_pg_failure_tests_20261004、dv_next；每次 approved_database 绑定单个获批阶段目标。
-- execution_confirmed 默认 FALSE；获批后先专用库前向回滚演练并填回退守卫、验收，通过后按获批范围同步 dv_next。
-- 专用库前向及严格回退演练、唯一性测试已通过，dv_next已同步；新备份dv_next_backup_20261004_181602已完整核验，禁止重复应用。
-- 仅改变 ux_product_master_master_sku、ux_product_neutral_product_code 的索引键：rtrim ASCII 尾空格 + public.erp_dictionary_ci。
-- 两个谓词均仅 is_deleted=false；不改 ux_product_neutral_master_color，不改列、数据、生成器、DI 或其他索引。
-- ICU 基线153.128仍须执行时核验，不宣称完整仿真 Chinese_PRC_CI_AS，尤其 U+200B 等差异。
-- 普通 DROP INDEX 必须短时取得最终表排他锁；固定 ONLY 主型号→中性顺序，不增加预备锁或咨询锁，不断开其他连接。
-- 等锁5秒、单语句30秒、事务60秒；任何失败停止并整事务 ROLLBACK，不重试提交、不清理冲突、不使用 CASCADE。
-- 新候选索引全部建成后才替换；非幂等，名称占用、结构偏离或重复应用立即拒绝。
-- 获批演练必须将末尾 COMMIT 替换为 ROLLBACK，保存目录输出供主控填写回退；回退演练可通过后才正式提交。
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
    -- 原定义依据本批主控提供的目录基线；执行时对本次绑定库逐字核验，偏差立即拒绝，不宣称本批 DDL 已验收。
    FOR expected IN SELECT * FROM (VALUES
        ('product_master', 'ux_product_master_master_sku',
         'CREATE UNIQUE INDEX ux_product_master_master_sku ON public.product_master USING btree (master_sku) WHERE (is_deleted = false)'),
        ('product_neutral', 'ux_product_neutral_product_code',
         'CREATE UNIQUE INDEX ux_product_neutral_product_code ON public.product_neutral USING btree (product_code) WHERE (is_deleted = false)')
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
    IF EXISTS (SELECT 1 FROM public.product_master WHERE is_deleted = FALSE
               GROUP BY rtrim(master_sku, ' ') COLLATE public.erp_dictionary_ci HAVING count(*) > 1)
       OR EXISTS (SELECT 1 FROM public.product_neutral WHERE is_deleted = FALSE
                  GROUP BY rtrim(product_code, ' ') COLLATE public.erp_dictionary_ci HAVING count(*) > 1) THEN
        RAISE EXCEPTION '拟议比较规则存在活动编码冲突，拒绝修改索引';
    END IF;
END
$preflight$;

-- 新索引全部建成后才替换旧索引；不用 CASCADE，任何未知依赖都会使整个事务失败。
CREATE UNIQUE INDEX ux_product_master_master_sku_ci_candidate
    ON public.product_master ((rtrim(master_sku, ' ') COLLATE public.erp_dictionary_ci)) WHERE is_deleted = FALSE;
CREATE UNIQUE INDEX ux_product_neutral_product_code_ci_candidate
    ON public.product_neutral ((rtrim(product_code, ' ') COLLATE public.erp_dictionary_ci)) WHERE is_deleted = FALSE;
DROP INDEX public.ux_product_master_master_sku;
DROP INDEX public.ux_product_neutral_product_code;
ALTER INDEX public.ux_product_master_master_sku_ci_candidate RENAME TO ux_product_master_master_sku;
ALTER INDEX public.ux_product_neutral_product_code_ci_candidate RENAME TO ux_product_neutral_product_code;

-- 演练及正式提交都保存此结果；精确表达式用于回退守卫，不猜测服务端规范化格式。
SELECT current_database() AS database_name, c.relname AS index_name,
       pg_get_indexdef(i.indexrelid) AS index_definition,
       pg_get_expr(i.indexprs, i.indrelid, FALSE) AS expression,
       pg_get_expr(i.indpred, i.indrelid, FALSE) AS predicate
FROM pg_catalog.pg_index i JOIN pg_catalog.pg_class c ON c.oid = i.indexrelid
WHERE i.indexrelid IN ('public.ux_product_master_master_sku'::regclass, 'public.ux_product_neutral_product_code'::regclass)
ORDER BY c.relname;
COMMIT;
