-- 父级型号默认装率可空增量；仅用于已有 PostgreSQL 库的 public.product_master。
-- 由维护人员确认目标实例和库后单独执行；不改写历史值，不调整实际 product_package 装率。
-- 原 ck_product_master_pcs CHECK (> 0) 保留：空值合法，非空值仍受正数约束保护。
BEGIN;
SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '60s';

ALTER TABLE "public"."product_master"
    ALTER COLUMN "default_pcs_per_package" DROP NOT NULL;

COMMENT ON COLUMN "public"."product_master"."default_pcs_per_package"
    IS '默认每箱装率（PCS/箱）；为空表示未设置，有值时必须为正整数。';

COMMIT;
