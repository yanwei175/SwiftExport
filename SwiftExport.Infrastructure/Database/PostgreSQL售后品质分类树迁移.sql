-- 售后品质分类增量迁移。仅在确认目标库后显式执行；旧字典与新维护数据均保留。
BEGIN;
SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '60s';
DO $$ BEGIN
    IF current_database() NOT IN ('dv_next', 'dv_next_pg_failure_tests_20261004') THEN
        RAISE EXCEPTION '目标库未在本次迁移范围内';
    END IF;
    IF to_regclass('public.sys_dict_data') IS NULL OR to_regclass('public.after_sales_header') IS NULL THEN
        RAISE EXCEPTION '缺少现有字典或售后表，拒绝迁移';
    END IF;
END $$;
-- 部署事务短时锁源表，避免预检与复制间变化；不用于日常业务锁。
LOCK TABLE public.sys_dict_data IN SHARE MODE;
CREATE TABLE IF NOT EXISTS "public"."after_sales_quality_category" (
    "uid" uuid NOT NULL,
    "version" integer NOT NULL DEFAULT 1,
    "is_deleted" boolean NOT NULL DEFAULT FALSE,
    "created_date" timestamp(6) without time zone NULL,
    "created_by" varchar(500) NULL,
    "updated_date" timestamp(6) without time zone NULL,
    "updated_by" varchar(500) NULL,
    "category_name" varchar(500) NOT NULL,
    "parent_id" uuid NULL,
    "fake_name" varchar(500) NULL,
    "category_code" varchar(300) NULL,
    "sort_no" integer NOT NULL,
    "is_enabled" boolean NOT NULL DEFAULT TRUE,
    CONSTRAINT "pk_after_sales_quality_category" PRIMARY KEY ("uid"),
    CONSTRAINT "ck_after_sales_quality_category_sort_no" CHECK ("sort_no" >= 0),
    CONSTRAINT "ck_after_sales_quality_category_version" CHECK ("version" >= 1)
);
CREATE INDEX IF NOT EXISTS "ix_after_sales_quality_category_parent_id" ON "public"."after_sales_quality_category" ("parent_id");
CREATE UNIQUE INDEX IF NOT EXISTS "ux_after_sales_quality_category_category_code" ON "public"."after_sales_quality_category" ((rtrim("category_code", ' ') COLLATE public.erp_dictionary_ci)) WHERE "is_deleted" = FALSE AND "category_code" IS NOT NULL;
-- 对已经存在的新表也核对完整形状；IF NOT EXISTS 不能作为相同结构的证据。
LOCK TABLE public.after_sales_quality_category IN ACCESS EXCLUSIVE MODE;
DO $$ BEGIN
    IF EXISTS (
        WITH expected(name, type_name, required, default_value) AS (VALUES
            ('uid','uuid',true,NULL::text), ('version','integer',true,'1'),
            ('is_deleted','boolean',true,'false'), ('created_date','timestamp(6) without time zone',false,NULL),
            ('created_by','character varying(500)',false,NULL), ('updated_date','timestamp(6) without time zone',false,NULL),
            ('updated_by','character varying(500)',false,NULL), ('category_name','character varying(500)',true,NULL),
            ('parent_id','uuid',false,NULL), ('fake_name','character varying(500)',false,NULL),
            ('category_code','character varying(300)',false,NULL), ('sort_no','integer',true,NULL),
            ('is_enabled','boolean',true,'true')),
        actual AS (SELECT a.attname::text AS name, format_type(a.atttypid,a.atttypmod) AS type_name,
            a.attnotnull AS required, pg_get_expr(d.adbin,d.adrelid) AS default_value
            FROM pg_attribute a LEFT JOIN pg_attrdef d ON d.adrelid=a.attrelid AND d.adnum=a.attnum
            WHERE a.attrelid='public.after_sales_quality_category'::regclass AND a.attnum>0 AND NOT a.attisdropped)
        SELECT 1 FROM expected e FULL JOIN actual a USING(name)
        WHERE e.name IS NULL OR a.name IS NULL OR e.type_name IS DISTINCT FROM a.type_name
            OR e.required IS DISTINCT FROM a.required OR e.default_value IS DISTINCT FROM a.default_value
    ) THEN RAISE EXCEPTION '新表字段类型、可空或默认值与契约不同'; END IF;
    IF (SELECT count(*) FROM pg_constraint WHERE conrelid='public.after_sales_quality_category'::regclass
            AND contype='p' AND pg_get_constraintdef(oid)='PRIMARY KEY (uid)') <> 1 THEN
        RAISE EXCEPTION '新表主键必须仅为 uid';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_index i JOIN pg_class c ON c.oid=i.indexrelid
        JOIN pg_am am ON am.oid=c.relam
        WHERE i.indrelid='public.after_sales_quality_category'::regclass
        AND c.relname='ux_after_sales_quality_category_category_code' AND i.indisunique AND i.indisvalid AND i.indisready
        AND i.indnkeyatts=1 AND i.indnatts=1 AND am.amname='btree'
        -- PG 将排序规则保存在 indcollation，单个索引键文本不包含 COLLATE。
        AND i.indcollation[0]=to_regcollation('public.erp_dictionary_ci')
        AND regexp_replace(lower(pg_get_indexdef(i.indexrelid,1,false)), '::text|public\.|[[:space:]()"]','','g')
            = 'rtrimcategory_code,'''''
        AND regexp_replace(lower(pg_get_expr(i.indpred,i.indrelid)), '[[:space:]()"]','','g')
            = 'is_deleted=falseandcategory_codeisnotnull') THEN
        RAISE EXCEPTION '分类编码唯一索引与产品分类比较规则不一致';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_index i JOIN pg_class c ON c.oid=i.indexrelid
        JOIN pg_am am ON am.oid=c.relam
        WHERE i.indrelid='public.after_sales_quality_category'::regclass
        AND c.relname='ix_after_sales_quality_category_parent_id' AND NOT i.indisunique AND i.indisvalid AND i.indisready
        AND i.indnkeyatts=1 AND i.indnatts=1 AND am.amname='btree' AND i.indpred IS NULL
        AND pg_get_indexdef(i.indexrelid,1,false)='parent_id') THEN
        RAISE EXCEPTION '分类父节点索引定义不一致';
    END IF;
END $$;
DO $$ BEGIN
    IF (SELECT count(*) FROM information_schema.columns WHERE table_schema='public' AND table_name='after_sales_quality_category') <> 13 THEN
        RAISE EXCEPTION '新表结构与本次契约不符，请先人工核对';
    END IF;
    IF EXISTS (SELECT 1 FROM public.sys_dict_data d
        WHERE rtrim(d.dict_code,' ') COLLATE public.erp_dictionary_ci = 'AfterSalesQualityCategory' COLLATE public.erp_dictionary_ci
        AND (COALESCE(NULLIF(btrim(d.data_name_zh),''),NULLIF(btrim(d.data_name_en),'')) IS NULL
             OR COALESCE(d.sort_order,0)<0 OR d.version<1)) THEN
        RAISE EXCEPTION '旧分类存在无有效名称、负排序或无效版本，拒绝静默修正';
    END IF;
    -- 创建来源一致才视为已迁入；其后名称/层级/版本/启用状态保持不变。
    IF EXISTS (SELECT 1 FROM public.sys_dict_data d JOIN public.after_sales_quality_category c ON c.uid=d.uid
        WHERE rtrim(d.dict_code,' ') COLLATE public.erp_dictionary_ci = 'AfterSalesQualityCategory' COLLATE public.erp_dictionary_ci
        AND (c.created_date IS DISTINCT FROM d.created_date OR c.created_by IS DISTINCT FROM d.created_by
            OR c.version<d.version OR (c.version=d.version AND (c.category_name IS DISTINCT FROM
                CASE WHEN NULLIF(btrim(d.data_name_zh),'') IS NOT NULL THEN d.data_name_zh ELSE d.data_name_en END
                OR c.fake_name IS DISTINCT FROM d.data_name_en OR c.is_enabled IS DISTINCT FROM d.is_enabled)))) THEN
        RAISE EXCEPTION '旧分类 Uid 与新表不同创建来源冲突，拒绝覆盖';
    END IF;
    IF EXISTS (SELECT 1 FROM public.after_sales_header h WHERE h.quality_category_id IS NOT NULL
        AND NOT EXISTS (SELECT 1 FROM public.after_sales_quality_category c WHERE c.uid=h.quality_category_id)
        AND NOT EXISTS (SELECT 1 FROM public.sys_dict_data d WHERE d.uid=h.quality_category_id
            AND rtrim(d.dict_code,' ') COLLATE public.erp_dictionary_ci = 'AfterSalesQualityCategory' COLLATE public.erp_dictionary_ci)) THEN
        RAISE EXCEPTION '售后存在无法迁入的品质分类引用，拒绝切换';
    END IF;
END $$;
INSERT INTO public.after_sales_quality_category
    (uid,version,is_deleted,created_date,created_by,updated_date,updated_by,
     category_name,parent_id,fake_name,category_code,sort_no,is_enabled)
SELECT d.uid,d.version,d.is_deleted,d.created_date,d.created_by,d.updated_date,d.updated_by,
    CASE WHEN NULLIF(btrim(d.data_name_zh),'') IS NOT NULL THEN d.data_name_zh ELSE d.data_name_en END,
    NULL,d.data_name_en,NULL,COALESCE(d.sort_order,0),d.is_enabled
FROM public.sys_dict_data d
WHERE rtrim(d.dict_code,' ') COLLATE public.erp_dictionary_ci = 'AfterSalesQualityCategory' COLLATE public.erp_dictionary_ci
ON CONFLICT (uid) DO NOTHING;
COMMENT ON TABLE "public"."after_sales_quality_category" IS '售后品质分类，独立保存层级、显示名称、别名和活动资格；父级与末级均可被售后单引用。';
COMMENT ON COLUMN "public"."after_sales_quality_category"."uid" IS '全链路唯一主键（Guid）。 说明：已统一以 Uid 作为主键标识，不再使用整型 Uid。';
COMMENT ON COLUMN "public"."after_sales_quality_category"."version" IS '并发版本号(乐观并发控制). 约定： 1. 新增时 Version = 1 2. 更新时 WHERE Uid + Version 命中后，Version 自动 +1 3. 命中失败视为并发冲突（数据已被他人修改）';
COMMENT ON COLUMN "public"."after_sales_quality_category"."is_deleted" IS '软删除标记；为真时业务查询默认不再返回该记录。';
COMMENT ON COLUMN "public"."after_sales_quality_category"."created_date" IS '记录创建时间。';
COMMENT ON COLUMN "public"."after_sales_quality_category"."created_by" IS '记录创建人标识。';
COMMENT ON COLUMN "public"."after_sales_quality_category"."updated_date" IS '记录最后更新时间。';
COMMENT ON COLUMN "public"."after_sales_quality_category"."updated_by" IS '记录最后更新人标识。';
COMMENT ON COLUMN "public"."after_sales_quality_category"."category_name" IS '分类显示名称。';
COMMENT ON COLUMN "public"."after_sales_quality_category"."parent_id" IS '上级分类身份；空值表示根节点。';
COMMENT ON COLUMN "public"."after_sales_quality_category"."fake_name" IS '分类别名，迁移时保留原英文名称。';
COMMENT ON COLUMN "public"."after_sales_quality_category"."category_code" IS '可空业务编码；旧字典英文名称不作为编码迁入。';
COMMENT ON COLUMN "public"."after_sales_quality_category"."sort_no" IS '非负显示排序号。';
COMMENT ON COLUMN "public"."after_sales_quality_category"."is_enabled" IS '是否允许新增引用；停用不抹除既有单据的历史显示。';
COMMIT;
