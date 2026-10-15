-- 产品颜色全局排序增量迁移。仅生成供评审和显式部署，不连接或重建数据库。
-- 首次添加字段时按旧维护页的唯一颜色编码顺序初始化；重复执行保留用户排序。
BEGIN;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'product_color' AND column_name = 'sort_no'
    ) THEN
        ALTER TABLE public.product_color ADD COLUMN sort_no integer NOT NULL DEFAULT 0;

        -- 锁定初始化范围，避免迁移期间的新增或修改混入排序基线；软删记录保持零。
        LOCK TABLE public.product_color IN SHARE ROW EXCLUSIVE MODE;
        WITH ranked AS (
            SELECT uid, row_number() OVER (
                ORDER BY color_code_unique COLLATE public.erp_dictionary_ci ASC NULLS FIRST, uid
            )::integer AS sort_no
            FROM public.product_color
            WHERE is_deleted = FALSE OR is_deleted IS NULL
        )
        UPDATE public.product_color color
        SET sort_no = ranked.sort_no
        FROM ranked WHERE color.uid = ranked.uid;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'public.product_color'::regclass AND conname = 'ck_product_color_sort_no'
    ) THEN
        ALTER TABLE public.product_color
            ADD CONSTRAINT ck_product_color_sort_no CHECK (sort_no >= 0);
    END IF;
END $$;

COMMENT ON COLUMN public.product_color.sort_no IS '全局显示排序号，数值越小越靠前；允许零和重复值。';

COMMIT;
