-- 本批用户授权测试库结构变更并同步dv_next；备份库和全部业务数据保留。
-- 来源查询和两索引统一为既有ICU + 尾普通空格规则；不改变来源文本、活动范围或业务锁。
-- 本脚本由主控串行执行，不由宿主自动部署。
BEGIN ISOLATION LEVEL READ COMMITTED;
SET LOCAL search_path = pg_catalog;
SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '30s';
SET LOCAL transaction_timeout = '60s';
DO $guard$
BEGIN
    IF current_database() NOT IN ('dv_next_pg_failure_tests_20261004','dv_next')
       OR inet_server_addr() IS DISTINCT FROM inet '192.168.3.12'
       OR inet_server_port() IS DISTINCT FROM 5432 THEN
        RAISE EXCEPTION '数据库或实例不属于来源索引授权范围';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_collation
        WHERE oid=to_regcollation('public.erp_dictionary_ci')
          AND collprovider='i' AND NOT collisdeterministic AND colllocale='und-u-ks-level2'
          AND collversion='153.128' AND pg_collation_actual_version(oid)=collversion) THEN
        RAISE EXCEPTION '既有ICU规则或版本变化，停止迁移';
    END IF;
END $guard$;
-- 在同一事务内锁表、检查原定义、建候选再替换；失败整体回滚，不清理重复业务行。
LOCK TABLE ONLY public.gross_demand IN ACCESS EXCLUSIVE MODE;
DO $preflight$
DECLARE expected record;
BEGIN
    FOR expected IN SELECT * FROM (VALUES
        ('ix_gross_demand_source',FALSE,'CREATE INDEX ix_gross_demand_source ON public.gross_demand USING btree (source_type, source_uid) WHERE (is_deleted = false)'),
        ('ux_gross_demand_source_line',TRUE,'CREATE UNIQUE INDEX ux_gross_demand_source_line ON public.gross_demand USING btree (source_type, source_uid, source_line_id) WHERE ((is_deleted = false) AND (lifecycle_status = 0))')
    ) AS definitions(index_name,is_unique,index_definition)
    LOOP
        IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_index i JOIN pg_catalog.pg_class x ON x.oid=i.indexrelid
            WHERE i.indexrelid=to_regclass('public.'||expected.index_name)
              AND i.indrelid=to_regclass('public.gross_demand')
              AND i.indisunique=expected.is_unique AND i.indisvalid AND i.indisready AND i.indislive
              AND NOT i.indisprimary AND NOT i.indisexclusion AND NOT i.indisreplident
              AND NOT i.indisclustered AND NOT i.indnullsnotdistinct
              AND x.reltablespace=0 AND x.reloptions IS NULL AND obj_description(x.oid,'pg_class') IS NULL
              AND pg_get_indexdef(i.indexrelid)=expected.index_definition
              AND NOT EXISTS(SELECT 1 FROM pg_catalog.pg_constraint c WHERE c.conindid=i.indexrelid)) THEN
            RAISE EXCEPTION '来源索引 % 定义变化，拒绝覆盖',expected.index_name;
        END IF;
        IF to_regclass('public.'||expected.index_name||'_ci_candidate') IS NOT NULL THEN
            RAISE EXCEPTION '来源候选索引名已占用';
        END IF;
    END LOOP;
    IF EXISTS (SELECT 1 FROM public.gross_demand WHERE NOT is_deleted AND lifecycle_status=0
        GROUP BY rtrim(source_type,' ') COLLATE public.erp_dictionary_ci,source_uid,source_line_id HAVING count(*)>1) THEN
        RAISE EXCEPTION '来源按查询规则已有重复，停止且不改数据';
    END IF;
END $preflight$;
CREATE INDEX ix_gross_demand_source_ci_candidate ON public.gross_demand ((rtrim(source_type,' ') COLLATE public.erp_dictionary_ci),source_uid) WHERE is_deleted=FALSE;
CREATE UNIQUE INDEX ux_gross_demand_source_line_ci_candidate ON public.gross_demand ((rtrim(source_type,' ') COLLATE public.erp_dictionary_ci),source_uid,source_line_id) WHERE is_deleted=FALSE AND lifecycle_status=0;
DROP INDEX public.ix_gross_demand_source;
ALTER INDEX public.ix_gross_demand_source_ci_candidate RENAME TO ix_gross_demand_source;
DROP INDEX public.ux_gross_demand_source_line;
ALTER INDEX public.ux_gross_demand_source_line_ci_candidate RENAME TO ux_gross_demand_source_line;
COMMIT;

