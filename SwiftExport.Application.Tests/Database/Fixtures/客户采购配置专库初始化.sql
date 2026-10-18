-- 仅限主控获得授权后，在全新专库 dv_customer_po_config_tests_20261018 执行。
-- 建库不由本脚本执行；目标须尚无用户表。初始化仅四张表及排序规则，不加载整仓数据。
-- 此夹具故意保留迁移前的编码索引及可空身份，以复现安全回填和拒绝重复；不是运行库建表入口。
BEGIN;
DO $$
BEGIN
    IF current_database()<>'dv_customer_po_config_tests_20261018' THEN
        RAISE EXCEPTION '仅允许全新客户采购配置专库';
    END IF;
    IF NULLIF(current_setting('swiftexport.expected_server',TRUE),'') IS NULL
       OR NULLIF(current_setting('swiftexport.expected_port',TRUE),'') IS NULL
       OR inet_server_addr() IS DISTINCT FROM current_setting('swiftexport.expected_server',TRUE)::inet
       OR current_setting('port') IS DISTINCT FROM current_setting('swiftexport.expected_port',TRUE) THEN
        RAISE EXCEPTION '先显式设置并核对已获授权的预期实例和端口';
    END IF;
    IF EXISTS(SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
        WHERE n.nspname NOT IN ('pg_catalog','information_schema') AND n.nspname NOT LIKE 'pg_toast%'
        AND c.relkind IN ('r','p','v','m','S','f')) THEN
        RAISE EXCEPTION '专库非空，拒绝覆盖或初始化已有对象';
    END IF;
END $$;
COMMENT ON DATABASE dv_customer_po_config_tests_20261018 IS 'SwiftExport.CustomerPoConfig.20261018.FreshFixture';
CREATE COLLATION public.erp_dictionary_ci (provider=icu, locale='und-u-ks-level2', deterministic=false);
CREATE TABLE "public"."customer" (
    "uid" uuid NOT NULL,
    "version" integer NOT NULL DEFAULT 1,
    "is_deleted" boolean NOT NULL DEFAULT FALSE,
    "created_date" timestamp(6) without time zone NULL,
    "created_by" varchar(500) NULL,
    "updated_date" timestamp(6) without time zone NULL,
    "updated_by" varchar(500) NULL,
    "is_active" boolean NOT NULL,
    "customer_code" varchar(300) NOT NULL,
    "customer_name" varchar(500) NULL,
    "address" varchar(2000) NULL,
    "phone_number" varchar(500) NULL,
    "contact_person" varchar(500) NULL,
    "label_folder_path" varchar(1000) NULL,
    "manual_folder_path" varchar(1000) NULL,
    "drawing_folder_path" varchar(1000) NULL,
    "linedrawing_folder_path" varchar(1000) NULL,
    "barcode_folder_path" varchar(1000) NULL,
    "spec_sheet_folder_path" varchar(1000) NULL,
    "doc_relate_no" varchar(300) NOT NULL,
    "incoterm_sort_remark" varchar(2000) NULL,
    "incoterm" varchar(500) NULL,
    "incoterm_location" varchar(500) NULL,
    "deposit_rate" numeric(18,6) NOT NULL,
    "lead_time_days" integer NOT NULL,
    "payment_method" varchar(500) NULL,
    "currency" varchar(500) NULL,
    "etd_sort_remark" varchar(2000) NULL,
    "shipped_by" varchar(500) NULL,
    CONSTRAINT "pk_customer" PRIMARY KEY ("uid"),
    CONSTRAINT "ck_customer_version" CHECK ("version" >= 1)
);

CREATE TABLE "public"."supplier" (
    "uid" uuid NOT NULL,
    "version" integer NOT NULL DEFAULT 1,
    "is_deleted" boolean NOT NULL DEFAULT FALSE,
    "created_date" timestamp(6) without time zone NULL,
    "created_by" varchar(500) NULL,
    "updated_date" timestamp(6) without time zone NULL,
    "updated_by" varchar(500) NULL,
    "is_active" boolean NOT NULL,
    "supplier_code" varchar(300) NOT NULL,
    "supplier_name" varchar(500) NOT NULL,
    "zhe_kou" numeric(18,4) NULL,
    "zhe_kou_bei_zhu" varchar(500) NULL,
    "doc_relate_no" varchar(300) NOT NULL,
    "contact_person" varchar(500) NULL,
    "contact_email" varchar(500) NULL,
    "contact_phone" varchar(500) NULL,
    "address" varchar(2000) NULL,
    "currency" varchar(500) NULL,
    "tax_rate" numeric(18,6) NULL,
    "payment_terms" varchar(500) NULL,
    "default_lead_time" integer NULL,
    CONSTRAINT "pk_supplier" PRIMARY KEY ("uid"),
    CONSTRAINT "ck_supplier_version" CHECK ("version" >= 1)
);

CREATE TABLE "public"."sys_dic_currency" (
    "uid" uuid NOT NULL,
    "version" integer NOT NULL DEFAULT 1,
    "is_deleted" boolean NOT NULL DEFAULT FALSE,
    "created_date" timestamp(6) without time zone NULL,
    "created_by" varchar(500) NULL,
    "updated_date" timestamp(6) without time zone NULL,
    "updated_by" varchar(500) NULL,
    "currency_code" varchar(300) NULL,
    "currency_name_en" varchar(500) NULL,
    "currency_name_zh" varchar(500) NULL,
    "currency_symbol" varchar(500) NULL,
    "precision" integer NOT NULL,
    "rate_to_base" numeric(18,6) NOT NULL,
    "is_base" boolean NOT NULL,
    "is_system" boolean NOT NULL,
    "is_enabled" boolean NOT NULL,
    "remark" varchar(2000) NULL,
    CONSTRAINT "pk_sys_dic_currency" PRIMARY KEY ("uid"),
    CONSTRAINT "ck_sys_dic_currency_precision" CHECK ("precision" >= 0 AND "precision" <= 8),
    CONSTRAINT "ck_sys_dic_currency_version" CHECK ("version" >= 1)
);

CREATE TABLE "public"."customer_po_config" (
    "uid" uuid NOT NULL,
    "version" integer NOT NULL DEFAULT 1,
    "is_deleted" boolean NOT NULL DEFAULT FALSE,
    "created_date" timestamp(6) without time zone NULL,
    "created_by" varchar(500) NULL,
    "updated_date" timestamp(6) without time zone NULL,
    "updated_by" varchar(500) NULL,
    "customer_id" uuid NULL,
    "customer_code" varchar(300) NULL,
    "supplier_id" uuid NULL,
    "supplier_code" varchar(300) NULL,
    "currency" varchar(10) NULL,
    "default_discount_rate" numeric(9,6) NULL,
    "discount_remark" varchar(500) NULL,
    "inlet_hose_type" varchar(300) NULL,
    "aerator_type" varchar(300) NULL,
    "pallet_type" varchar(300) NULL,
    "box_type" varchar(300) NULL,
    "label_type" varchar(300) NULL,
    "im_type" varchar(300) NULL,
    "carton_type" varchar(300) NULL,
    "logo_type" varchar(300) NULL,
    "sales_person" varchar(500) NULL,
    "purchaser" varchar(500) NULL,
    "note" varchar(2000) NULL,
    CONSTRAINT "pk_customer_po_config" PRIMARY KEY ("uid"),
    CONSTRAINT "ck_customer_po_config_default_discount_rate" CHECK ("default_discount_rate" IS NULL OR "default_discount_rate" >= 0),
    CONSTRAINT "ck_customer_po_config_version" CHECK ("version" >= 1)
);
CREATE UNIQUE INDEX ux_customer_po_config_codes ON public.customer_po_config
 ((rtrim(customer_code,' ') COLLATE public.erp_dictionary_ci),(rtrim(supplier_code,' ') COLLATE public.erp_dictionary_ci))
 NULLS NOT DISTINCT WHERE is_deleted=FALSE;
-- 固定的专库夹具身份，只供本轮验收；运行库脚本不引用这些身份。
INSERT INTO public.customer(uid,is_active,customer_code,doc_relate_no,deposit_rate,lead_time_days)
VALUES('11111111-1111-4111-8111-111111111111',TRUE,'C-CONFIG','C-CONFIG',0,0);
INSERT INTO public.supplier(uid,is_active,supplier_code,supplier_name,doc_relate_no)
VALUES('22222222-2222-4222-8222-222222222222',TRUE,'S-CONFIG','配置测试供应商','S-CONFIG');
INSERT INTO public.sys_dic_currency(uid,currency_code,precision,rate_to_base,is_base,is_system,is_enabled)
VALUES('33333333-3333-4333-8333-333333333333','CNY',2,1,TRUE,TRUE,TRUE);
INSERT INTO public.customer_po_config(uid,customer_code,supplier_code,default_discount_rate)
VALUES('44444444-4444-4444-8444-444444444444','C-CONFIG','S-CONFIG',1.05);
COMMIT;
