-- 仅供管理员在指定业务库维护时段升级；不会删除或改写历史单据。
SELECT btrim(bo_no, ' ') COLLATE public.erp_dictionary_ci AS bo_no,
       count(*) AS order_count, array_agg(uid ORDER BY uid) AS order_ids
FROM public.purchase_order_head
WHERE is_deleted=FALSE AND bo_no IS NOT NULL AND btrim(bo_no, ' ')<>''
GROUP BY btrim(bo_no, ' ') COLLATE public.erp_dictionary_ci HAVING count(*)>1;

BEGIN;
-- 防止预检和建索引之间插入重复号；发现重复直接拒绝，不自动清理。
LOCK TABLE public.purchase_order_head IN SHARE MODE;
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM public.purchase_order_head
        WHERE is_deleted=FALSE AND bo_no IS NOT NULL AND btrim(bo_no, ' ')<>''
        GROUP BY btrim(bo_no, ' ') COLLATE public.erp_dictionary_ci HAVING count(*)>1
    ) THEN
        RAISE EXCEPTION '存在未删除采购订单的重复 BONO，请人工处理后重试';
    END IF;
END $$;
CREATE UNIQUE INDEX IF NOT EXISTS ux_purchase_order_head_bo_no
ON public.purchase_order_head ((btrim(bo_no, ' ') COLLATE public.erp_dictionary_ci))
WHERE is_deleted=FALSE AND bo_no IS NOT NULL AND btrim(bo_no, ' ')<>'';
COMMIT;
