-- 分类共享标签升级为字典属性模板及统一对象选值；不迁移试用标签数据。
-- 生产执行前：备份、退出旧标签维护客户端、停止相关 API 写入，由操作者清理四张标签表全部记录。
-- 本脚本只检查空表，不自动删除数据；不 DROP/CREATE 选择表，原地保留表及 BaseEntity 字段。
-- 使用独立会话一次执行全文；上次执行已导致事务失败时，先单独执行 ROLLBACK。
-- 专用测试库预演仅替换 expected_database 为 dv_next_product_attribute_tests_20261012。
-- 前置条件：已完成主型号产品系列字段改名，product_master 应包含 product_series，不再包含 product_tag。
BEGIN;
SELECT set_config('swiftexport.expected_database', 'dv_next', true);
SELECT set_config('swiftexport.expected_server', '192.168.3.12', true);
SELECT set_config('swiftexport.expected_port', '5432', true);
SET LOCAL lock_timeout = '15s';

DO $$
BEGIN
    -- inet 比较兼容服务器地址带 /32 的表示；本机 Unix socket 或未知实例一律拒绝。
    IF current_database() <> current_setting('swiftexport.expected_database')
       OR inet_server_addr() IS DISTINCT FROM current_setting('swiftexport.expected_server')::inet
       OR current_setting('port') <> current_setting('swiftexport.expected_port') THEN
        RAISE EXCEPTION '目标实例或库名不符，拒绝属性模板迁移：库=%，IP=%，端口=%',
            current_database(), inet_server_addr(), current_setting('port');
    END IF;
    IF to_regclass('public.product_attribute_group') IS NULL
       OR to_regclass('public.product_attribute_option') IS NULL
       OR to_regclass('public.product_category_attribute_group') IS NULL
       OR to_regclass('public.product_master_attribute_selection') IS NULL
       OR to_regclass('public.product_master') IS NULL
       OR to_regclass('public.sys_dict_data') IS NULL THEN
        RAISE EXCEPTION '缺少旧标签表、主型号表或字典明细表，拒绝迁移';
    END IF;
    IF to_regclass('public.product_attribute_profile_group') IS NOT NULL
       OR to_regclass('public.product_attribute_selection') IS NOT NULL
       OR to_regprocedure('public.fn_product_attribute_profile_group_immutable_identity()') IS NOT NULL THEN
        RAISE EXCEPTION '新标签结构已经存在；请核对迁移状态，不允许覆盖或重复执行';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public'
        AND table_name='product_master' AND column_name='product_series')
       OR EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public'
        AND table_name='product_master' AND column_name='product_tag') THEN
        RAISE EXCEPTION '请先完成主型号产品系列字段改名，再执行属性模板迁移';
    END IF;
END $$;

-- 先冻结旧事实再检查空表，防止检查后有旧客户端插入记录；锁超时会失败回滚，不无限等待。
LOCK TABLE public.product_attribute_group, public.product_attribute_option,
    public.product_category_attribute_group, public.product_master_attribute_selection,
    public.product_master IN ACCESS EXCLUSIVE MODE;

DO $$
DECLARE
    group_count bigint;
    option_count bigint;
    relation_count bigint;
    selection_count bigint;
BEGIN
    -- 标签表必须符合已经实施的全局组／分类关联结构，额外未知列也要求人工检查。
    IF EXISTS (
        WITH expected(table_name, column_name, data_type, nullable) AS (VALUES
            ('product_attribute_group','uid','uuid','NO'),
            ('product_attribute_group','version','integer','NO'),
            ('product_attribute_group','is_deleted','boolean','NO'),
            ('product_attribute_group','created_date','timestamp without time zone','YES'),
            ('product_attribute_group','created_by','character varying','YES'),
            ('product_attribute_group','updated_date','timestamp without time zone','YES'),
            ('product_attribute_group','updated_by','character varying','YES'),
            ('product_attribute_group','group_code','character varying','NO'),
            ('product_attribute_group','group_name','character varying','NO'),
            ('product_attribute_group','selection_mode','integer','NO'),
            ('product_attribute_group','sort_order','integer','NO'),
            ('product_attribute_option','uid','uuid','NO'),
            ('product_attribute_option','version','integer','NO'),
            ('product_attribute_option','is_deleted','boolean','NO'),
            ('product_attribute_option','created_date','timestamp without time zone','YES'),
            ('product_attribute_option','created_by','character varying','YES'),
            ('product_attribute_option','updated_date','timestamp without time zone','YES'),
            ('product_attribute_option','updated_by','character varying','YES'),
            ('product_attribute_option','attribute_group_id','uuid','NO'),
            ('product_attribute_option','option_code','character varying','NO'),
            ('product_attribute_option','option_name','character varying','NO'),
            ('product_attribute_option','sort_order','integer','NO'),
            ('product_category_attribute_group','uid','uuid','NO'),
            ('product_category_attribute_group','version','integer','NO'),
            ('product_category_attribute_group','is_deleted','boolean','NO'),
            ('product_category_attribute_group','created_date','timestamp without time zone','YES'),
            ('product_category_attribute_group','created_by','character varying','YES'),
            ('product_category_attribute_group','updated_date','timestamp without time zone','YES'),
            ('product_category_attribute_group','updated_by','character varying','YES'),
            ('product_category_attribute_group','category_id','uuid','NO'),
            ('product_category_attribute_group','attribute_group_id','uuid','NO'),
            ('product_master_attribute_selection','uid','uuid','NO'),
            ('product_master_attribute_selection','version','integer','NO'),
            ('product_master_attribute_selection','is_deleted','boolean','NO'),
            ('product_master_attribute_selection','created_date','timestamp without time zone','YES'),
            ('product_master_attribute_selection','created_by','character varying','YES'),
            ('product_master_attribute_selection','updated_date','timestamp without time zone','YES'),
            ('product_master_attribute_selection','updated_by','character varying','YES'),
            ('product_master_attribute_selection','product_master_id','uuid','NO'),
            ('product_master_attribute_selection','option_id','uuid','NO'))
        SELECT 1 FROM expected e FULL JOIN (
            SELECT table_name, column_name, data_type, is_nullable AS nullable
            FROM information_schema.columns WHERE table_schema='public'
              AND table_name IN ('product_attribute_group','product_attribute_option',
                'product_category_attribute_group','product_master_attribute_selection')
        ) a USING (table_name,column_name)
        WHERE e.column_name IS NULL OR a.column_name IS NULL
           OR e.data_type <> a.data_type OR e.nullable <> a.nullable
    ) THEN
        RAISE EXCEPTION '标签列不符合旧全局组／分类关联结构，拒绝迁移';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
        WHERE table_schema='public' AND table_name='product_master' AND column_name='product_type'
          AND data_type='character varying' AND character_maximum_length=300 AND is_nullable='YES')
       OR EXISTS (SELECT 1 FROM information_schema.columns
        WHERE table_schema='public' AND table_name='product_master' AND column_name='product_attribute_profile_id') THEN
        RAISE EXCEPTION '主型号旧类型列结构不符或模板列已存在，拒绝迁移';
    END IF;
    IF EXISTS (
        WITH expected(index_name, table_name, unique_index, columns) AS (VALUES
            ('ux_product_category_attribute_group_category_group','product_category_attribute_group',true,ARRAY['category_id','attribute_group_id']::text[]),
            ('ix_product_category_attribute_group_group','product_category_attribute_group',false,ARRAY['attribute_group_id']::text[]),
            ('ux_product_master_attribute_selection_master_option','product_master_attribute_selection',true,ARRAY['product_master_id','option_id']::text[]),
            ('ix_product_master_attribute_selection_option','product_master_attribute_selection',false,ARRAY['option_id']::text[]))
        SELECT 1 FROM expected e
        LEFT JOIN pg_class x ON x.oid=to_regclass('public.' || e.index_name)
        LEFT JOIN pg_index i ON i.indexrelid=x.oid
        LEFT JOIN LATERAL (SELECT array_agg(a.attname::text ORDER BY k.ordinality) AS columns
            FROM unnest(i.indkey::smallint[]) WITH ORDINALITY k(attnum,ordinality)
            JOIN pg_attribute a ON a.attrelid=i.indrelid AND a.attnum=k.attnum) c ON true
        WHERE i.indexrelid IS NULL OR i.indrelid <> to_regclass('public.' || e.table_name)
          OR i.indisunique <> e.unique_index OR NOT i.indisvalid OR NOT i.indisready
          OR c.columns IS DISTINCT FROM e.columns
          OR regexp_replace(lower(pg_get_expr(i.indpred,i.indrelid)), '[\s"()]', '', 'g') IS DISTINCT FROM 'is_deleted=false'
    ) THEN
        RAISE EXCEPTION '旧关联或选择索引结构不符，拒绝迁移';
    END IF;
    IF EXISTS (
        WITH expected(table_name, constraint_name, kind) AS (VALUES
            ('product_category_attribute_group','pk_product_category_attribute_group','p'),
            ('product_category_attribute_group','ck_product_category_attribute_group_version','c'),
            ('product_master_attribute_selection','pk_product_master_attribute_selection','p'),
            ('product_master_attribute_selection','ck_product_master_attribute_selection_version','c'))
        SELECT 1 FROM expected e LEFT JOIN pg_constraint c
          ON c.conrelid=to_regclass('public.' || e.table_name) AND c.conname=e.constraint_name
        WHERE c.oid IS NULL OR c.contype::text <> e.kind
    ) OR EXISTS (SELECT 1 FROM pg_constraint WHERE contype='f'
        AND (conrelid IN ('public.product_category_attribute_group'::regclass,'public.product_master_attribute_selection'::regclass)
          OR confrelid IN ('public.product_category_attribute_group'::regclass,'public.product_master_attribute_selection'::regclass))) THEN
        RAISE EXCEPTION '旧表约束不符或发现物理外键，须先人工核对，拒绝迁移';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_trigger
        WHERE tgrelid='public.product_category_attribute_group'::regclass
          AND tgname='tr_product_category_attribute_group_immutable_identity'
          AND tgfoid=to_regprocedure('public.fn_product_category_attribute_group_immutable_identity()')
          AND NOT tgisinternal)
       OR EXISTS (SELECT 1 FROM pg_trigger WHERE NOT tgisinternal
          AND tgrelid IN ('public.product_category_attribute_group'::regclass,'public.product_master_attribute_selection'::regclass)
          AND tgname <> 'tr_product_category_attribute_group_immutable_identity') THEN
        RAISE EXCEPTION '旧身份保护触发器不符或存在未知触发器，拒绝迁移';
    END IF;

    SELECT count(*) INTO group_count FROM public.product_attribute_group;
    SELECT count(*) INTO option_count FROM public.product_attribute_option;
    SELECT count(*) INTO relation_count FROM public.product_category_attribute_group;
    SELECT count(*) INTO selection_count FROM public.product_master_attribute_selection;
    IF group_count <> 0 OR option_count <> 0 OR relation_count <> 0 OR selection_count <> 0 THEN
        RAISE EXCEPTION '须由操作者先清理全部试用标签记录（包括软删记录），本脚本不清数据：组=%，选项=%，分类关联=%，主型号选择=%',
            group_count, option_count, relation_count, selection_count;
    END IF;
    IF EXISTS (SELECT 1 FROM public.product_master WHERE NULLIF(btrim(product_type), '') IS NOT NULL) THEN
        RAISE EXCEPTION '主型号旧 product_type 存在非空内容（包括软删记录），请先人工确认，不允许丢弃后迁移';
    END IF;
END $$;

-- 模板关联沿用原表，不保留旧分类身份或继承入口；替换函数体防止引用已改名的 category_id。
DROP TRIGGER tr_product_category_attribute_group_immutable_identity ON public.product_category_attribute_group;
DROP FUNCTION public.fn_product_category_attribute_group_immutable_identity();
ALTER TABLE public.product_category_attribute_group RENAME TO product_attribute_profile_group;
ALTER TABLE public.product_attribute_profile_group RENAME COLUMN category_id TO product_attribute_profile_id;
ALTER TABLE public.product_attribute_profile_group RENAME CONSTRAINT pk_product_category_attribute_group TO pk_product_attribute_profile_group;
ALTER TABLE public.product_attribute_profile_group RENAME CONSTRAINT ck_product_category_attribute_group_version TO ck_product_attribute_profile_group_version;
ALTER INDEX public.ux_product_category_attribute_group_category_group RENAME TO ux_product_attribute_profile_group_profile_group;
ALTER INDEX public.ix_product_category_attribute_group_group RENAME TO ix_product_attribute_profile_group_group;
CREATE FUNCTION public.fn_product_attribute_profile_group_immutable_identity()
RETURNS trigger LANGUAGE plpgsql AS $body$
BEGIN
    IF NEW.product_attribute_profile_id IS DISTINCT FROM OLD.product_attribute_profile_id
       OR NEW.attribute_group_id IS DISTINCT FROM OLD.attribute_group_id THEN
        RAISE EXCEPTION USING ERRCODE = 'P3101', MESSAGE = '成立身份不可修改';
    END IF;
    RETURN NEW;
END;
$body$;
CREATE TRIGGER tr_product_attribute_profile_group_immutable_identity
BEFORE UPDATE ON public.product_attribute_profile_group FOR EACH ROW
EXECUTE FUNCTION public.fn_product_attribute_profile_group_immutable_identity();

-- 对象类型必须每次明确填写；不设置 ProductMaster 默认值，以免后续对象被错误归属。
ALTER TABLE public.product_master_attribute_selection RENAME TO product_attribute_selection;
ALTER TABLE public.product_attribute_selection RENAME COLUMN product_master_id TO owner_id;
ALTER TABLE public.product_attribute_selection ADD COLUMN owner_type integer NOT NULL;
ALTER TABLE public.product_attribute_selection RENAME CONSTRAINT pk_product_master_attribute_selection TO pk_product_attribute_selection;
ALTER TABLE public.product_attribute_selection RENAME CONSTRAINT ck_product_master_attribute_selection_version TO ck_product_attribute_selection_version;
ALTER TABLE public.product_attribute_selection ADD CONSTRAINT ck_product_attribute_selection_owner_type CHECK (owner_type IN (1,2,3,4));
ALTER TABLE public.product_attribute_selection ADD CONSTRAINT ck_product_attribute_selection_owner_id CHECK (owner_id <> '00000000-0000-0000-0000-000000000000'::uuid);
DROP INDEX public.ux_product_master_attribute_selection_master_option;
CREATE UNIQUE INDEX ux_product_attribute_selection_owner_option
    ON public.product_attribute_selection (owner_type,owner_id,option_id) WHERE is_deleted=FALSE;
ALTER INDEX public.ix_product_master_attribute_selection_option RENAME TO ix_product_attribute_selection_option;

-- 模板使用字典明细逻辑引用，分类与产品系列保持独立；旧空字符串类型列正式退出。
ALTER TABLE public.product_master ADD COLUMN product_attribute_profile_id uuid NULL;
CREATE INDEX ix_product_master_attribute_profile ON public.product_master (product_attribute_profile_id)
    WHERE is_deleted=FALSE AND product_attribute_profile_id IS NOT NULL;
ALTER TABLE public.product_master DROP COLUMN product_type;

COMMENT ON TABLE public.product_attribute_group IS '可由多个属性模板共享的标签组，统一定义选项与单多选规则。';
COMMENT ON COLUMN public.product_attribute_option.attribute_group_id IS '所属标签组的稳定身份；适用范围由组与属性模板的直接关联决定。';
COMMENT ON TABLE public.product_attribute_profile_group IS '属性模板直接关联的共享标签组；模板来源于产品属性模板字典明细。';
COMMENT ON COLUMN public.product_attribute_profile_group.product_attribute_profile_id IS '适用的字典模板明细身份，创建后不可修改。';
COMMENT ON COLUMN public.product_attribute_profile_group.attribute_group_id IS '共享标签组身份，创建后不可修改。';
COMMENT ON TABLE public.product_attribute_selection IS '各产品对象独立选择的标签值；对象类型与身份共同确定归属，不复制模板或组身份。';
COMMENT ON COLUMN public.product_attribute_selection.owner_type IS '所属对象的固定业务类型；必须与 OwnerId 指向的实体一致。';
COMMENT ON COLUMN public.product_attribute_selection.owner_id IS '所属对象的技术身份，由对象自身的保存服务校验和维护。';
COMMENT ON COLUMN public.product_attribute_selection.option_id IS '实际选中的标签选项身份。';
COMMENT ON COLUMN public.product_master.product_attribute_profile_id IS '产品属性模板的字典明细身份；为空时不提供标签候选。';
COMMIT;
