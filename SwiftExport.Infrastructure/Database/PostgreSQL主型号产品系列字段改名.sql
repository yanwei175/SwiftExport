-- 主型号产品系列字段改名；由用户在已确认的目标数据库中手动执行。
-- 执行前备份并停止旧后端/客户端，改名后配套升级；不改变可空性、长度或已有值。
-- 若上次事务失败，先单独执行 ROLLBACK；本脚本不重复执行。
BEGIN;
ALTER TABLE public.product_master RENAME COLUMN product_tag TO product_series;
COMMENT ON TABLE public.product_master IS '产品主型号实体，保存主型号层级的默认描述、分类和系列名。';
COMMENT ON COLUMN public.product_master.product_series IS '产品系列名；为空表示未设置，不参与标签选择。';
COMMIT;
