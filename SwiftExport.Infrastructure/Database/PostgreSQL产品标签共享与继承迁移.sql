-- 产品标签 V1（组属于分类）升级为 V2（全局组、多分类直接关联、祖先继承）。
-- 生产库由操作者备份后手动执行；退出旧标签维护客户端并停止相关写入。
-- 专用测试库验收只修改 expected_database，不改变服务器、端口或业务数据修复规则。
-- 一次执行全文件。上次事务失败时先单独 ROLLBACK；不支持重复执行或自动修复旧数据。
BEGIN;
SELECT set_config('swiftexport.expected_database', 'dv_next', true);
SELECT set_config('swiftexport.expected_server', '192.168.3.12', true);
SELECT set_config('swiftexport.expected_port', '5432', true);

DO $$
BEGIN
    IF current_database() <> current_setting('swiftexport.expected_database')
       OR host(inet_server_addr()) IS DISTINCT FROM current_setting('swiftexport.expected_server')
       OR current_setting('port') <> current_setting('swiftexport.expected_port') THEN
        RAISE EXCEPTION '目标实例或库名不符，拒绝标签共享迁移：库=%，IP=%，端口=%',
            current_database(), host(inet_server_addr()), current_setting('port');
    END IF;
    IF to_regclass('public.product_attribute_group') IS NULL
       OR to_regclass('public.product_attribute_option') IS NULL
       OR to_regclass('public.product_master_attribute_selection') IS NULL
       OR to_regclass('public.product_category') IS NULL
       OR to_regclass('public.product_master') IS NULL THEN
        RAISE EXCEPTION '缺少 V1 标签表或既有产品分类/主型号表，拒绝迁移';
    END IF;
    IF to_regclass('public.product_category_attribute_group') IS NOT NULL THEN
        RAISE EXCEPTION '分类标签关联表已存在；请核对是否已迁移，不允许覆盖或重复迁移';
    END IF;
    IF to_regcollation('public.erp_dictionary_ci') IS NULL THEN
        RAISE EXCEPTION '缺少 public.erp_dictionary_ci 编码比较基线，拒绝迁移';
    END IF;
END $$;

-- 冻结旧定义及其引用，使预检与转换使用同一事实；不修改既有分类、产品或选择。
LOCK TABLE public.product_category, public.product_master,
    public.product_attribute_group, public.product_attribute_option,
    public.product_master_attribute_selection IN SHARE ROW EXCLUSIVE MODE;

DO $$
DECLARE
    invalid_count bigint;
BEGIN
    -- 严格核对 V1 标签列及类型，未知结构必须人工核对，不能用 IF NOT EXISTS 掩盖。
    IF EXISTS (
        WITH expected(table_name, column_name, data_type, nullable) AS (VALUES
            ('product_attribute_group','uid','uuid','NO'),
            ('product_attribute_group','version','integer','NO'),
            ('product_attribute_group','is_deleted','boolean','NO'),
            ('product_attribute_group','created_date','timestamp without time zone','YES'),
            ('product_attribute_group','created_by','character varying','YES'),
            ('product_attribute_group','updated_date','timestamp without time zone','YES'),
            ('product_attribute_group','updated_by','character varying','YES'),
            ('product_attribute_group','category_id','uuid','NO'),
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
              AND table_name IN ('product_attribute_group','product_attribute_option','product_master_attribute_selection')
        ) a USING (table_name,column_name)
        WHERE e.column_name IS NULL OR a.column_name IS NULL
           OR e.data_type <> a.data_type OR e.nullable <> a.nullable
    ) THEN
        RAISE EXCEPTION '标签表列不符合 V1 结构，拒绝迁移';
    END IF;
    IF to_regclass('public.ux_product_attribute_group_category_code') IS NULL
       OR to_regclass('public.ix_product_attribute_group_category_order') IS NULL
       OR to_regclass('public.ux_product_attribute_group_code') IS NOT NULL THEN
        RAISE EXCEPTION 'V1 分类内组编码索引不符或 V2 索引已存在，拒绝迁移';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_collation WHERE oid='public.erp_dictionary_ci'::regcollation
        AND collprovider='i' AND NOT collisdeterministic)
       OR NOT ('Unit' COLLATE public.erp_dictionary_ci = 'unit'
           AND rtrim('Unit ', ' ') COLLATE public.erp_dictionary_ci = 'Unit'
           AND 'A' COLLATE public.erp_dictionary_ci = 'Ａ'
           AND 'あ' COLLATE public.erp_dictionary_ci = 'ア'
           AND 'e' COLLATE public.erp_dictionary_ci <> 'é') THEN
        RAISE EXCEPTION 'erp_dictionary_ci 不符合当前 ICU 编码比较基线，拒绝迁移';
    END IF;
    IF EXISTS (SELECT 1 FROM pg_collation WHERE oid='public.erp_dictionary_ci'::regcollation
        AND collversion IS DISTINCT FROM pg_collation_actual_version(oid)) THEN
        RAISE EXCEPTION 'ICU 实际版本与编码排序规则记录不符，请先核对排序规则及索引，拒绝迁移';
    END IF;
    SELECT count(*) INTO invalid_count FROM public.product_attribute_group g
        LEFT JOIN public.product_category c ON c.uid=g.category_id
        WHERE c.uid IS NULL OR (NOT g.is_deleted AND c.is_deleted);
    IF invalid_count > 0 THEN
        RAISE EXCEPTION '存在 % 个标签组引用缺失或无效分类，拒绝迁移', invalid_count;
    END IF;
    SELECT count(*) INTO invalid_count FROM (
        SELECT rtrim(group_code, ' ') COLLATE public.erp_dictionary_ci
        FROM public.product_attribute_group WHERE NOT is_deleted
        GROUP BY rtrim(group_code, ' ') COLLATE public.erp_dictionary_ci HAVING count(*) > 1
    ) duplicates;
    IF invalid_count > 0 THEN
        RAISE EXCEPTION '存在 % 组按实际 ICU 比较重复的全局标签组编码，请先人工处理，拒绝迁移', invalid_count;
    END IF;
    SELECT count(*) INTO invalid_count FROM public.product_attribute_option o
        LEFT JOIN public.product_attribute_group g ON g.uid=o.attribute_group_id
        WHERE g.uid IS NULL OR (NOT o.is_deleted AND g.is_deleted);
    IF invalid_count > 0 THEN
        RAISE EXCEPTION '存在 % 个选项引用缺失或无效组，拒绝迁移', invalid_count;
    END IF;
    SELECT count(*) INTO invalid_count FROM public.product_master_attribute_selection s
        LEFT JOIN public.product_master m ON m.uid=s.product_master_id
        LEFT JOIN public.product_attribute_option o ON o.uid=s.option_id
        LEFT JOIN public.product_attribute_group g ON g.uid=o.attribute_group_id
        WHERE m.uid IS NULL OR o.uid IS NULL OR g.uid IS NULL
           OR (NOT s.is_deleted AND (m.is_deleted OR o.is_deleted OR g.is_deleted
                                     OR m.category_id IS DISTINCT FROM g.category_id));
    IF invalid_count > 0 THEN
        RAISE EXCEPTION '存在 % 条选择引用缺失、失效或不适用的旧定义，拒绝迁移', invalid_count;
    END IF;
END $$;

CREATE TABLE public.product_category_attribute_group (
    uid uuid NOT NULL,
    version integer NOT NULL DEFAULT 1,
    is_deleted boolean NOT NULL DEFAULT FALSE,
    created_date timestamp(6) without time zone NULL,
    created_by varchar(500) NULL,
    updated_date timestamp(6) without time zone NULL,
    updated_by varchar(500) NULL,
    category_id uuid NOT NULL,
    attribute_group_id uuid NOT NULL,
    CONSTRAINT pk_product_category_attribute_group PRIMARY KEY (uid),
    CONSTRAINT ck_product_category_attribute_group_version CHECK (version >= 1)
);
CREATE UNIQUE INDEX ux_product_category_attribute_group_category_group
    ON public.product_category_attribute_group (category_id, attribute_group_id) WHERE is_deleted = FALSE;
CREATE INDEX ix_product_category_attribute_group_group
    ON public.product_category_attribute_group (attribute_group_id) WHERE is_deleted = FALSE;
-- 与实体 DbImmutableColumns 生成器一致，分类和组身份只允许通过删除、新增变更。
CREATE FUNCTION public.fn_product_category_attribute_group_immutable_identity()
RETURNS trigger LANGUAGE plpgsql AS $body$
BEGIN
    IF NEW.category_id IS DISTINCT FROM OLD.category_id
       OR NEW.attribute_group_id IS DISTINCT FROM OLD.attribute_group_id THEN
        RAISE EXCEPTION USING ERRCODE = 'P3101', MESSAGE = '成立身份不可修改';
    END IF;
    RETURN NEW;
END;
$body$;
CREATE TRIGGER tr_product_category_attribute_group_immutable_identity
BEFORE UPDATE ON public.product_category_attribute_group FOR EACH ROW
EXECUTE FUNCTION public.fn_product_category_attribute_group_immutable_identity();

-- 旧归属转换成一条直接关联，复制原定义的版本、审计与软删除信息；不复制继承结果。
INSERT INTO public.product_category_attribute_group
    (uid, version, is_deleted, created_date, created_by, updated_date, updated_by, category_id, attribute_group_id)
SELECT gen_random_uuid(), version, is_deleted, created_date, created_by, updated_date, updated_by, category_id, uid
FROM public.product_attribute_group;

DO $$
BEGIN
    IF (SELECT count(*) FROM public.product_category_attribute_group)
       <> (SELECT count(*) FROM public.product_attribute_group)
       OR EXISTS (SELECT 1 FROM public.product_attribute_group g
           LEFT JOIN public.product_category_attribute_group r ON r.attribute_group_id=g.uid
           WHERE r.uid IS NULL OR r.category_id<>g.category_id OR r.version<>g.version
               OR r.is_deleted<>g.is_deleted OR r.created_date IS DISTINCT FROM g.created_date
               OR r.created_by IS DISTINCT FROM g.created_by OR r.updated_date IS DISTINCT FROM g.updated_date
               OR r.updated_by IS DISTINCT FROM g.updated_by) THEN
        RAISE EXCEPTION '旧分类归属转换核验失败，全部回滚';
    END IF;
END $$;

DROP INDEX public.ux_product_attribute_group_category_code;
DROP INDEX public.ix_product_attribute_group_category_order;
ALTER TABLE public.product_attribute_group DROP COLUMN category_id;
CREATE UNIQUE INDEX ux_product_attribute_group_code
    ON public.product_attribute_group ((rtrim(group_code, ' ') COLLATE public.erp_dictionary_ci)) WHERE is_deleted = FALSE;
CREATE INDEX ix_product_attribute_group_order ON public.product_attribute_group (sort_order) WHERE is_deleted = FALSE;

COMMENT ON TABLE public.product_attribute_group IS '全局产品标签组，可通过分类关联在多个分类及其后代共用。';
COMMENT ON COLUMN public.product_attribute_group.group_code IS '有效记录中全局唯一的组编码，保存时规范首尾空白。';
COMMENT ON TABLE public.product_category_attribute_group IS '分类直接关联的全局标签组；祖先继承在读取时计算，不复制到后代。';
COMMENT ON COLUMN public.product_category_attribute_group.uid IS '全链路唯一主键（Guid）。 说明：已统一以 Uid 作为主键标识，不再使用整型 Uid。';
COMMENT ON COLUMN public.product_category_attribute_group.version IS '并发版本号(乐观并发控制). 约定： 1. 新增时 Version = 1 2. 更新时 WHERE Uid + Version 命中后，Version 自动 +1 3. 命中失败视为并发冲突（数据已被他人修改）';
COMMENT ON COLUMN public.product_category_attribute_group.is_deleted IS '软删除标记；为真时业务查询默认不再返回该记录。';
COMMENT ON COLUMN public.product_category_attribute_group.created_date IS '记录创建时间。';
COMMENT ON COLUMN public.product_category_attribute_group.created_by IS '记录创建人标识。';
COMMENT ON COLUMN public.product_category_attribute_group.updated_date IS '记录最后更新时间。';
COMMENT ON COLUMN public.product_category_attribute_group.updated_by IS '记录最后更新人标识。';
COMMENT ON COLUMN public.product_category_attribute_group.category_id IS '直接应用标签组的分类身份；子分类通过祖先链继承，不复制关联。';
COMMENT ON COLUMN public.product_category_attribute_group.attribute_group_id IS '可被多个分类共用的全局标签组身份；创建后不得迁移关联身份。';
COMMIT;
