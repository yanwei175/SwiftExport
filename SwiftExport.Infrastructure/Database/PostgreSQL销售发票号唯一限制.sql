-- 销售发票号唯一限制：仅供管理员在指定业务库人工升级，不自动删除或修改历史订单。
-- 第一步只读检查：存在结果时，先人工处理重复草稿或改正发票号，再执行下方事务。
SELECT btrim(invoice_no, ' ') COLLATE public.erp_dictionary_ci AS invoice_no,
       count(*) AS order_count, array_agg(uid ORDER BY uid) AS order_ids
FROM public.sales_order_header
WHERE is_deleted=FALSE AND invoice_no IS NOT NULL AND btrim(invoice_no, ' ')<>''
GROUP BY btrim(invoice_no, ' ') COLLATE public.erp_dictionary_ci
HAVING count(*)>1;

BEGIN;
-- 建索引期间阻止并发写入，保证预检查与创建之间没有新增重复；只在维护时段执行。
LOCK TABLE public.sales_order_header IN SHARE MODE;
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM public.sales_order_header
        WHERE is_deleted=FALSE AND invoice_no IS NOT NULL AND btrim(invoice_no, ' ')<>''
        GROUP BY btrim(invoice_no, ' ') COLLATE public.erp_dictionary_ci HAVING count(*)>1
    ) THEN
        RAISE EXCEPTION '存在未软删除的重复发票号，请人工处理后再执行；本脚本不会删除订单';
    END IF;
END $$;

CREATE UNIQUE INDEX IF NOT EXISTS ux_sales_order_header_invoice_no
ON public.sales_order_header ((btrim(invoice_no, ' ') COLLATE public.erp_dictionary_ci))
WHERE is_deleted=FALSE AND invoice_no IS NOT NULL AND btrim(invoice_no, ' ')<>'';
COMMIT;
