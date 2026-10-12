-- 产品分类标签增量迁移：只创建三张标签表、索引和中文注释，不修改现有产品或旧 ProductTag。
-- 默认目标按已确认实例填写。生产库需由操作者手动执行；测试时修改 expected_database。
-- 若上次执行造成事务失败，先单独执行 ROLLBACK；独立会话一次执行全文件。
-- 与实体结构生成预览一致，重复执行会失败，不能用 IF NOT EXISTS 掩盖结构差异。
BEGIN;
SELECT set_config('swiftexport.expected_database', 'dv_next', true);
SELECT set_config('swiftexport.expected_server', '192.168.3.12', true);
SELECT set_config('swiftexport.expected_port', '5432', true);
DO $$
BEGIN
    IF current_database() <> current_setting('swiftexport.expected_database')
       OR host(inet_server_addr()) IS DISTINCT FROM current_setting('swiftexport.expected_server')
       OR current_setting('port') <> current_setting('swiftexport.expected_port') THEN
        RAISE EXCEPTION '目标实例或库名不符，拒绝产品分类标签迁移：库=%，IP=%，端口=%', current_database(), host(inet_server_addr()), current_setting('port');
    END IF;
    IF to_regcollation('public.erp_dictionary_ci') IS NULL THEN
        RAISE EXCEPTION '缺少当前主数据编码比较基线 public.erp_dictionary_ci，请先核对现有数据库基线';
    END IF;
END $$;
CREATE TABLE "public"."product_attribute_group" (
    "uid" uuid NOT NULL,
    "version" integer NOT NULL DEFAULT 1,
    "is_deleted" boolean NOT NULL DEFAULT FALSE,
    "created_date" timestamp(6) without time zone NULL,
    "created_by" varchar(500) NULL,
    "updated_date" timestamp(6) without time zone NULL,
    "updated_by" varchar(500) NULL,
    "category_id" uuid NOT NULL,
    "group_code" varchar(100) NOT NULL,
    "group_name" varchar(200) NOT NULL,
    "selection_mode" integer NOT NULL,
    "sort_order" integer NOT NULL DEFAULT 0,
    CONSTRAINT "pk_product_attribute_group" PRIMARY KEY ("uid"),
    CONSTRAINT "ck_product_attribute_group_mode" CHECK ("selection_mode" IN (0,1)),
    CONSTRAINT "ck_product_attribute_group_text" CHECK (length(btrim("group_code")) > 0 AND length(btrim("group_name")) > 0),
    CONSTRAINT "ck_product_attribute_group_version" CHECK ("version" >= 1)
);
CREATE INDEX "ix_product_attribute_group_category_order" ON "public"."product_attribute_group" ("category_id", "sort_order") WHERE "is_deleted" = FALSE;
CREATE UNIQUE INDEX "ux_product_attribute_group_category_code" ON "public"."product_attribute_group" ("category_id", (rtrim("group_code", ' ') COLLATE public.erp_dictionary_ci)) WHERE "is_deleted" = FALSE;
COMMENT ON TABLE "public"."product_attribute_group" IS '某个产品分类下的标签组，定义可选值的分组与单多选规则。';
COMMENT ON COLUMN "public"."product_attribute_group"."uid" IS '全链路唯一主键（Guid）。 说明：已统一以 Uid 作为主键标识，不再使用整型 Uid。';
COMMENT ON COLUMN "public"."product_attribute_group"."version" IS '并发版本号(乐观并发控制). 约定： 1. 新增时 Version = 1 2. 更新时 WHERE Uid + Version 命中后，Version 自动 +1 3. 命中失败视为并发冲突（数据已被他人修改）';
COMMENT ON COLUMN "public"."product_attribute_group"."is_deleted" IS '软删除标记；为真时业务查询默认不再返回该记录。';
COMMENT ON COLUMN "public"."product_attribute_group"."created_date" IS '记录创建时间。';
COMMENT ON COLUMN "public"."product_attribute_group"."created_by" IS '记录创建人标识。';
COMMENT ON COLUMN "public"."product_attribute_group"."updated_date" IS '记录最后更新时间。';
COMMENT ON COLUMN "public"."product_attribute_group"."updated_by" IS '记录最后更新人标识。';
COMMENT ON COLUMN "public"."product_attribute_group"."category_id" IS '所属分类的稳定身份；创建后不迁移到另一分类。';
COMMENT ON COLUMN "public"."product_attribute_group"."group_code" IS '分类内唯一的组编码，保存时规范首尾空白。';
COMMENT ON COLUMN "public"."product_attribute_group"."group_name" IS '用于标签维护与产品展示的组名称。';
COMMENT ON COLUMN "public"."product_attribute_group"."selection_mode" IS '本组的单选或多选规则。';
COMMENT ON COLUMN "public"."product_attribute_group"."sort_order" IS '组的显示顺序，同序时按 Uid 保持稳定。';
CREATE TABLE "public"."product_attribute_option" (
    "uid" uuid NOT NULL,
    "version" integer NOT NULL DEFAULT 1,
    "is_deleted" boolean NOT NULL DEFAULT FALSE,
    "created_date" timestamp(6) without time zone NULL,
    "created_by" varchar(500) NULL,
    "updated_date" timestamp(6) without time zone NULL,
    "updated_by" varchar(500) NULL,
    "attribute_group_id" uuid NOT NULL,
    "option_code" varchar(100) NOT NULL,
    "option_name" varchar(200) NOT NULL,
    "sort_order" integer NOT NULL DEFAULT 0,
    CONSTRAINT "pk_product_attribute_option" PRIMARY KEY ("uid"),
    CONSTRAINT "ck_product_attribute_option_text" CHECK (length(btrim("option_code")) > 0 AND length(btrim("option_name")) > 0),
    CONSTRAINT "ck_product_attribute_option_version" CHECK ("version" >= 1)
);
CREATE INDEX "ix_product_attribute_option_group_order" ON "public"."product_attribute_option" ("attribute_group_id", "sort_order") WHERE "is_deleted" = FALSE;
CREATE UNIQUE INDEX "ux_product_attribute_option_group_code" ON "public"."product_attribute_option" ("attribute_group_id", (rtrim("option_code", ' ') COLLATE public.erp_dictionary_ci)) WHERE "is_deleted" = FALSE;
COMMENT ON TABLE "public"."product_attribute_option" IS '分类标签组中的受控选项，名称可以维护，归属组保持不变。';
COMMENT ON COLUMN "public"."product_attribute_option"."uid" IS '全链路唯一主键（Guid）。 说明：已统一以 Uid 作为主键标识，不再使用整型 Uid。';
COMMENT ON COLUMN "public"."product_attribute_option"."version" IS '并发版本号(乐观并发控制). 约定： 1. 新增时 Version = 1 2. 更新时 WHERE Uid + Version 命中后，Version 自动 +1 3. 命中失败视为并发冲突（数据已被他人修改）';
COMMENT ON COLUMN "public"."product_attribute_option"."is_deleted" IS '软删除标记；为真时业务查询默认不再返回该记录。';
COMMENT ON COLUMN "public"."product_attribute_option"."created_date" IS '记录创建时间。';
COMMENT ON COLUMN "public"."product_attribute_option"."created_by" IS '记录创建人标识。';
COMMENT ON COLUMN "public"."product_attribute_option"."updated_date" IS '记录最后更新时间。';
COMMENT ON COLUMN "public"."product_attribute_option"."updated_by" IS '记录最后更新人标识。';
COMMENT ON COLUMN "public"."product_attribute_option"."attribute_group_id" IS '所属标签组的稳定身份，支持创建后通过关联取得分类。';
COMMENT ON COLUMN "public"."product_attribute_option"."option_code" IS '组内唯一的选项编码。';
COMMENT ON COLUMN "public"."product_attribute_option"."option_name" IS '用于选择与展示的选项名称。';
COMMENT ON COLUMN "public"."product_attribute_option"."sort_order" IS '选项显示顺序，同序时按 Uid 保持稳定。';
CREATE TABLE "public"."product_master_attribute_selection" (
    "uid" uuid NOT NULL,
    "version" integer NOT NULL DEFAULT 1,
    "is_deleted" boolean NOT NULL DEFAULT FALSE,
    "created_date" timestamp(6) without time zone NULL,
    "created_by" varchar(500) NULL,
    "updated_date" timestamp(6) without time zone NULL,
    "updated_by" varchar(500) NULL,
    "product_master_id" uuid NOT NULL,
    "option_id" uuid NOT NULL,
    CONSTRAINT "pk_product_master_attribute_selection" PRIMARY KEY ("uid"),
    CONSTRAINT "ck_product_master_attribute_selection_version" CHECK ("version" >= 1)
);
CREATE INDEX "ix_product_master_attribute_selection_option" ON "public"."product_master_attribute_selection" ("option_id") WHERE "is_deleted" = FALSE;
CREATE UNIQUE INDEX "ux_product_master_attribute_selection_master_option" ON "public"."product_master_attribute_selection" ("product_master_id", "option_id") WHERE "is_deleted" = FALSE;
COMMENT ON TABLE "public"."product_master_attribute_selection" IS '主型号实际选中的标签值；分类和组均由选项关联取得。';
COMMENT ON COLUMN "public"."product_master_attribute_selection"."uid" IS '全链路唯一主键（Guid）。 说明：已统一以 Uid 作为主键标识，不再使用整型 Uid。';
COMMENT ON COLUMN "public"."product_master_attribute_selection"."version" IS '并发版本号(乐观并发控制). 约定： 1. 新增时 Version = 1 2. 更新时 WHERE Uid + Version 命中后，Version 自动 +1 3. 命中失败视为并发冲突（数据已被他人修改）';
COMMENT ON COLUMN "public"."product_master_attribute_selection"."is_deleted" IS '软删除标记；为真时业务查询默认不再返回该记录。';
COMMENT ON COLUMN "public"."product_master_attribute_selection"."created_date" IS '记录创建时间。';
COMMENT ON COLUMN "public"."product_master_attribute_selection"."created_by" IS '记录创建人标识。';
COMMENT ON COLUMN "public"."product_master_attribute_selection"."updated_date" IS '记录最后更新时间。';
COMMENT ON COLUMN "public"."product_master_attribute_selection"."updated_by" IS '记录最后更新人标识。';
COMMENT ON COLUMN "public"."product_master_attribute_selection"."product_master_id" IS '选择关系所属的主型号身份。';
COMMENT ON COLUMN "public"."product_master_attribute_selection"."option_id" IS '实际选中的标签选项身份。';
COMMIT;
