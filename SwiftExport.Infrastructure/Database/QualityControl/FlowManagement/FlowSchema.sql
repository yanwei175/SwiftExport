-- 品质管理／流量管理结构增量。仅用于已有当前 PostgreSQL 基线的库；不自动连接或部署。
-- 表、索引、CHECK、身份保护和说明来自实体生成基线；不建立数据库外键。
-- 本结构增量仅执行一次，字典初始化使用独立可重入脚本。
BEGIN;
DO $guard$
BEGIN
    IF to_regclass('public.product') IS NULL OR to_regclass('public.sys_dict_type') IS NULL OR to_regclass('public.sys_dict_data') IS NULL THEN
        RAISE EXCEPTION '缺少当前物料或字典结构，拒绝建立流量模块';
    END IF;
    IF to_regclass('public.flow_header') IS NOT NULL OR to_regclass('public.flow_line') IS NOT NULL THEN
        RAISE EXCEPTION '流量模块表已存在，请核对结构；拒绝覆盖或部分初始化';
    END IF;
END;
$guard$;
CREATE TABLE "public"."flow_header" (
    "uid" uuid NOT NULL,
    "version" integer NOT NULL DEFAULT 1,
    "is_deleted" boolean NOT NULL DEFAULT FALSE,
    "created_date" timestamp(6) without time zone NULL,
    "created_by" varchar(500) NULL,
    "updated_date" timestamp(6) without time zone NULL,
    "updated_by" varchar(500) NULL,
    "material_id" uuid NOT NULL,
    "sku_snapshot" varchar(300) NOT NULL,
    "test_date" date NOT NULL,
    "market_description" varchar(2000) NULL,
    "configuration_description" varchar(2000) NULL,
    "remark" varchar(2000) NULL,
    CONSTRAINT "pk_flow_header" PRIMARY KEY ("uid"),
    CONSTRAINT "ck_flow_header_version" CHECK ("version" >= 1)
);
CREATE TABLE "public"."flow_line" (
    "uid" uuid NOT NULL,
    "version" integer NOT NULL DEFAULT 1,
    "is_deleted" boolean NOT NULL DEFAULT FALSE,
    "created_date" timestamp(6) without time zone NULL,
    "created_by" varchar(500) NULL,
    "updated_date" timestamp(6) without time zone NULL,
    "updated_by" varchar(500) NULL,
    "header_id" uuid NOT NULL,
    "function_id" uuid NOT NULL,
    "function_name_snapshot" varchar(500) NOT NULL,
    "limiter_specification_id" uuid NOT NULL,
    "limiter_specification_name_snapshot" varchar(500) NOT NULL,
    "flow_1_bar_l_min" numeric(18,4) NULL,
    "flow_2_bar_l_min" numeric(18,4) NULL,
    "flow_3_bar_l_min" numeric(18,4) NULL,
    "flow_4_bar_l_min" numeric(18,4) NULL,
    "flow_5_bar_l_min" numeric(18,4) NULL,
    "flow_6_bar_l_min" numeric(18,4) NULL,
    "flow_7_bar_l_min" numeric(18,4) NULL,
    "flow_8_bar_l_min" numeric(18,4) NULL,
    "flow_20_psi_gpm" numeric(18,4) NULL,
    "flow_40_psi_gpm" numeric(18,4) NULL,
    "flow_60_psi_gpm" numeric(18,4) NULL,
    "flow_80_psi_gpm" numeric(18,4) NULL,
    "flow_100_psi_gpm" numeric(18,4) NULL,
    "sort_no" integer NOT NULL,
    "remark" varchar(2000) NULL,
    CONSTRAINT "pk_flow_line" PRIMARY KEY ("uid"),
    CONSTRAINT "ck_flow_line_has_measurement" CHECK (COALESCE("flow_1_bar_l_min", "flow_2_bar_l_min", "flow_3_bar_l_min", "flow_4_bar_l_min", "flow_5_bar_l_min", "flow_6_bar_l_min", "flow_7_bar_l_min", "flow_8_bar_l_min", "flow_20_psi_gpm", "flow_40_psi_gpm", "flow_60_psi_gpm", "flow_80_psi_gpm", "flow_100_psi_gpm") IS NOT NULL),
    CONSTRAINT "ck_flow_line_measurements" CHECK (("flow_1_bar_l_min" IS NULL OR "flow_1_bar_l_min" >= 0) AND ("flow_2_bar_l_min" IS NULL OR "flow_2_bar_l_min" >= 0) AND ("flow_3_bar_l_min" IS NULL OR "flow_3_bar_l_min" >= 0) AND ("flow_4_bar_l_min" IS NULL OR "flow_4_bar_l_min" >= 0) AND ("flow_5_bar_l_min" IS NULL OR "flow_5_bar_l_min" >= 0) AND ("flow_6_bar_l_min" IS NULL OR "flow_6_bar_l_min" >= 0) AND ("flow_7_bar_l_min" IS NULL OR "flow_7_bar_l_min" >= 0) AND ("flow_8_bar_l_min" IS NULL OR "flow_8_bar_l_min" >= 0) AND ("flow_20_psi_gpm" IS NULL OR "flow_20_psi_gpm" >= 0) AND ("flow_40_psi_gpm" IS NULL OR "flow_40_psi_gpm" >= 0) AND ("flow_60_psi_gpm" IS NULL OR "flow_60_psi_gpm" >= 0) AND ("flow_80_psi_gpm" IS NULL OR "flow_80_psi_gpm" >= 0) AND ("flow_100_psi_gpm" IS NULL OR "flow_100_psi_gpm" >= 0)),
    CONSTRAINT "ck_flow_line_sort_no" CHECK ("sort_no" >= 0),
    CONSTRAINT "ck_flow_line_version" CHECK ("version" >= 1)
);
CREATE INDEX "ix_flow_header_date" ON "public"."flow_header" ("test_date", "uid") WHERE "is_deleted" = FALSE;
CREATE INDEX "ix_flow_header_material_date" ON "public"."flow_header" ("material_id", "test_date", "uid") WHERE "is_deleted" = FALSE;
CREATE FUNCTION public."fn_flow_header_immutable_identity"() RETURNS trigger LANGUAGE plpgsql AS $body$ BEGIN IF NEW."material_id" IS DISTINCT FROM OLD."material_id" OR NEW."sku_snapshot" IS DISTINCT FROM OLD."sku_snapshot" THEN RAISE EXCEPTION USING ERRCODE = 'P3101', MESSAGE = '成立身份不可修改'; END IF; RETURN NEW; END; $body$;
CREATE TRIGGER "tr_flow_header_immutable_identity" BEFORE UPDATE ON "public"."flow_header" FOR EACH ROW EXECUTE FUNCTION public."fn_flow_header_immutable_identity"();
CREATE UNIQUE INDEX "ux_flow_line_header_function" ON "public"."flow_line" ("header_id", "function_id") WHERE "is_deleted" = FALSE;
COMMENT ON TABLE "public"."flow_header" IS '独立保存某物料在一套配件配置下的一次流量测试；物料身份与 SKU 快照创建后固定。';
COMMENT ON COLUMN "public"."flow_header"."uid" IS '全链路唯一主键（Guid）。 说明：已统一以 Uid 作为主键标识，不再使用整型 Uid。';
COMMENT ON COLUMN "public"."flow_header"."version" IS '并发版本号(乐观并发控制). 约定： 1. 新增时 Version = 1 2. 更新时 WHERE Uid + Version 命中后，Version 自动 +1 3. 命中失败视为并发冲突（数据已被他人修改）';
COMMENT ON COLUMN "public"."flow_header"."is_deleted" IS '软删除标记；为真时业务查询默认不再返回该记录。';
COMMENT ON COLUMN "public"."flow_header"."created_date" IS '记录创建时间。';
COMMENT ON COLUMN "public"."flow_header"."created_by" IS '记录创建人标识。';
COMMENT ON COLUMN "public"."flow_header"."updated_date" IS '记录最后更新时间。';
COMMENT ON COLUMN "public"."flow_header"."updated_by" IS '记录最后更新人标识。';
COMMENT ON COLUMN "public"."flow_header"."material_id" IS '测试物料身份，仅逻辑关联，不建立数据库外键。';
COMMENT ON COLUMN "public"."flow_header"."sku_snapshot" IS '首次测试建档时的物料 SKU，后续物料改名不回写。';
COMMENT ON COLUMN "public"."flow_header"."test_date" IS '实际测试日期，不包含时间或时区。';
COMMENT ON COLUMN "public"."flow_header"."market_description" IS '本次测试的市场和方法背景说明。';
COMMENT ON COLUMN "public"."flow_header"."configuration_description" IS '各功能共用的配件配置说明；各自限流器规格保存在行上。';
COMMENT ON COLUMN "public"."flow_header"."remark" IS '测试组补充说明。';
COMMENT ON TABLE "public"."flow_line" IS '保存本测试组中一个功能的限流器配置和固定压力点实测结果；两套压力列使用各自固定单位。';
COMMENT ON COLUMN "public"."flow_line"."uid" IS '全链路唯一主键（Guid）。 说明：已统一以 Uid 作为主键标识，不再使用整型 Uid。';
COMMENT ON COLUMN "public"."flow_line"."version" IS '并发版本号(乐观并发控制). 约定： 1. 新增时 Version = 1 2. 更新时 WHERE Uid + Version 命中后，Version 自动 +1 3. 命中失败视为并发冲突（数据已被他人修改）';
COMMENT ON COLUMN "public"."flow_line"."is_deleted" IS '软删除标记；为真时业务查询默认不再返回该记录。';
COMMENT ON COLUMN "public"."flow_line"."created_date" IS '记录创建时间。';
COMMENT ON COLUMN "public"."flow_line"."created_by" IS '记录创建人标识。';
COMMENT ON COLUMN "public"."flow_line"."updated_date" IS '记录最后更新时间。';
COMMENT ON COLUMN "public"."flow_line"."updated_by" IS '记录最后更新人标识。';
COMMENT ON COLUMN "public"."flow_line"."header_id" IS '所属流量测试组身份。';
COMMENT ON COLUMN "public"."flow_line"."function_id" IS '流量测试功能字典项身份。';
COMMENT ON COLUMN "public"."flow_line"."function_name_snapshot" IS '采用该功能字典项时的显示名称，普通编辑不追随字典改名。';
COMMENT ON COLUMN "public"."flow_line"."limiter_specification_id" IS '本功能使用的限流器规格字典项身份，“不限流”也必须明确选择。';
COMMENT ON COLUMN "public"."flow_line"."limiter_specification_name_snapshot" IS '采用该限流器规格时的显示名称，不作为产品实测流量。';
COMMENT ON COLUMN "public"."flow_line"."flow_1_bar_l_min" IS '1 bar 压力下的实测流量，单位 L/min；空表示未测。';
COMMENT ON COLUMN "public"."flow_line"."flow_2_bar_l_min" IS '2 bar 压力下的实测流量，单位 L/min；空表示未测。';
COMMENT ON COLUMN "public"."flow_line"."flow_3_bar_l_min" IS '3 bar 压力下的实测流量，单位 L/min；空表示未测。';
COMMENT ON COLUMN "public"."flow_line"."flow_4_bar_l_min" IS '4 bar 压力下的实测流量，单位 L/min；空表示未测。';
COMMENT ON COLUMN "public"."flow_line"."flow_5_bar_l_min" IS '5 bar 压力下的实测流量，单位 L/min；空表示未测。';
COMMENT ON COLUMN "public"."flow_line"."flow_6_bar_l_min" IS '6 bar 压力下的实测流量，单位 L/min；空表示未测。';
COMMENT ON COLUMN "public"."flow_line"."flow_7_bar_l_min" IS '7 bar 压力下的实测流量，单位 L/min；空表示未测。';
COMMENT ON COLUMN "public"."flow_line"."flow_8_bar_l_min" IS '8 bar 压力下的实测流量，单位 L/min；空表示未测。';
COMMENT ON COLUMN "public"."flow_line"."flow_20_psi_gpm" IS '20 PSI 压力下的实测流量，单位美国加仑/分钟；空表示未测。';
COMMENT ON COLUMN "public"."flow_line"."flow_40_psi_gpm" IS '40 PSI 压力下的实测流量，单位美国加仑/分钟；空表示未测。';
COMMENT ON COLUMN "public"."flow_line"."flow_60_psi_gpm" IS '60 PSI 压力下的实测流量，单位美国加仑/分钟；空表示未测。';
COMMENT ON COLUMN "public"."flow_line"."flow_80_psi_gpm" IS '80 PSI 压力下的实测流量，单位美国加仑/分钟；空表示未测。';
COMMENT ON COLUMN "public"."flow_line"."flow_100_psi_gpm" IS '100 PSI 压力下的实测流量，单位美国加仑/分钟；空表示未测。';
COMMENT ON COLUMN "public"."flow_line"."sort_no" IS '非负显示排序号。';
COMMENT ON COLUMN "public"."flow_line"."remark" IS '具体限流器型号、特殊配件或测量备注。';
COMMIT;
