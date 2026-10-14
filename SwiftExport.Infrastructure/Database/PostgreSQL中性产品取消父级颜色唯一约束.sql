-- 中性产品以 ProductCode 全局唯一；同一父级可以有多个不同编码且颜色相同的记录。
-- 本脚本由管理员确认目标实例及数据库后手工执行，不由应用启动或构建自动执行。
-- 仅删除旧父级＋颜色唯一索引，不修改数据、全局编码唯一索引或普通查询索引。
-- 任一步失败应停止并 ROLLBACK；首次部署可把末尾 COMMIT 改为 ROLLBACK 演练。
BEGIN;
SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '30s';

-- 先取得 DROP INDEX 所需的表锁，让定义核验与删除之间不被并发 DDL 改变。
LOCK TABLE ONLY public.product_neutral IN ACCESS EXCLUSIVE MODE;

DO $guard$
DECLARE
    old_index oid := to_regclass('public.ux_product_neutral_master_color');
BEGIN
    -- 全局业务编码约束必须仍在；不以撤除旧规则为由接纳编码重复。
    IF NOT EXISTS (
        SELECT 1 FROM pg_catalog.pg_index
        WHERE indexrelid = to_regclass('public.ux_product_neutral_product_code')
          AND indrelid = 'public.product_neutral'::regclass
          AND indisunique AND indisvalid AND indisready
    ) THEN
        RAISE EXCEPTION '中性产品全局编码唯一索引缺失或无效，停止迁移';
    END IF;

    -- 允许重复执行；同名对象的定义不符时拒绝删除，避免影响其他约束。
    IF old_index IS NOT NULL AND NOT EXISTS (
        SELECT 1 FROM pg_catalog.pg_index
        WHERE indexrelid = old_index AND indrelid = 'public.product_neutral'::regclass
          AND indisunique AND NOT indisprimary AND indnkeyatts = 2 AND indnatts = 2
          AND indexprs IS NULL
          AND pg_get_indexdef(old_index, 1, TRUE) = 'product_master_id'
          AND pg_get_indexdef(old_index, 2, TRUE) = 'color_id'
          AND pg_get_expr(indpred, indrelid) = '(is_deleted = false)'
          AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_constraint WHERE conindid = old_index)
    ) THEN
        RAISE EXCEPTION '旧父级颜色索引定义不符或被其他约束引用，停止迁移';
    END IF;
END;
$guard$;

-- 不使用 CASCADE；未知依赖会使整个事务失败。
DROP INDEX IF EXISTS public.ux_product_neutral_master_color;
COMMIT;
