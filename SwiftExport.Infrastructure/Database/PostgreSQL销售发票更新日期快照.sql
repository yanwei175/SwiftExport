-- 销售单头新增发票更新日期快照。确认目标业务库后显式执行；不回填或改写历史单据。
BEGIN;
SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '60s';

ALTER TABLE public.sales_order_header
    ADD COLUMN IF NOT EXISTS invoice_update_date timestamp(6) without time zone NULL;
COMMENT ON COLUMN public.sales_order_header.invoice_update_date
    IS '原单据的发票更新日期快照，可空；不参与金额、交期或需求计算。';

COMMIT;
