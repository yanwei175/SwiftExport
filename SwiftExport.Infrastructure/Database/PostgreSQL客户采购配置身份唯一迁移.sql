-- 客户采购默认配置：由编码对改为未删除记录中的客户 ID + 供应商 ID 唯一。
-- 仅交付人工审核脚本，不自动部署。先停用旧写入入口，确认备份和目标实例/库，再授权执行。
-- 执行者须在同一连接显式设置 swiftexport.expected_database / expected_server / expected_port；
-- 本脚本没有默认运行库，不以当前连接反填“预期目标”。失败后整个事务回滚，不删数据。
BEGIN;
SET LOCAL lock_timeout = '15s';
SET LOCAL statement_timeout = '60s';
DO $$
BEGIN
    IF NULLIF(current_setting('swiftexport.expected_database',TRUE),'') IS NULL
       OR NULLIF(current_setting('swiftexport.expected_server',TRUE),'') IS NULL
       OR NULLIF(current_setting('swiftexport.expected_port',TRUE),'') IS NULL THEN
        RAISE EXCEPTION '必须先显式指定预期实例、端口及库名，拒绝自动采用当前连接目标';
    END IF;
    IF current_database() <> current_setting('swiftexport.expected_database')
       OR inet_server_addr() IS DISTINCT FROM current_setting('swiftexport.expected_server')::inet
       OR current_setting('port') <> current_setting('swiftexport.expected_port') THEN
        RAISE EXCEPTION '实际实例或库名与已确认目标不符，拒绝迁移';
    END IF;
    IF to_regclass('public.customer') IS NULL OR to_regclass('public.supplier') IS NULL
       OR to_regclass('public.customer_po_config') IS NULL THEN
        RAISE EXCEPTION '客户、供应商或采购默认配置表缺失，拒绝迁移';
    END IF;
    IF to_regclass('public.ux_customer_po_config_parties') IS NOT NULL THEN
        RAISE EXCEPTION '身份唯一索引已存在，请先核对是否已经完成迁移，禁止重复改写';
    END IF;
    IF NOT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public'
        AND table_name='customer_po_config' AND column_name='is_deleted'
        AND data_type='boolean' AND is_nullable='NO') THEN
        RAISE EXCEPTION '配置软删除标记必须为非空布尔列，拒绝让 NULL 记录绕过活动身份检查和唯一索引';
    END IF;
    IF to_regclass('public.ux_customer_po_config_codes') IS NOT NULL AND NOT EXISTS (
        SELECT 1 FROM pg_index WHERE indexrelid='public.ux_customer_po_config_codes'::regclass
          AND indrelid='public.customer_po_config'::regclass AND indisunique AND indisvalid
          AND indnkeyatts=2 AND pg_get_indexdef(indexrelid) LIKE '%customer_code%'
          AND pg_get_indexdef(indexrelid) LIKE '%supplier_code%') THEN
        RAISE EXCEPTION '旧编码索引结构不符，不能移除未知对象';
    END IF;
END $$;

-- 同一事务锁住主数据与配置，验证期间不能发生主数据改名、删除或并发配置写入。
LOCK TABLE public.customer, public.supplier IN SHARE MODE;
LOCK TABLE public.customer_po_config IN ACCESS EXCLUSIVE MODE;

-- 非空身份只按身份匹配，不因身份失效而猜测改绑；仅 NULL 身份允许用旧编码唯一回填。
CREATE TEMP TABLE customer_po_config_party_plan ON COMMIT DROP AS
WITH customer_matches AS (
    SELECT cpc.uid AS config_id,c.uid,c.customer_code,count(*) OVER(PARTITION BY cpc.uid) AS matches
    FROM public.customer_po_config cpc JOIN public.customer c
      ON COALESCE(c.is_deleted,FALSE)=FALSE AND c.uid<>'00000000-0000-0000-0000-000000000000'::uuid
     AND ((cpc.customer_id IS NOT NULL AND c.uid=cpc.customer_id)
       OR (cpc.customer_id IS NULL AND rtrim(c.customer_code,' ') COLLATE public.erp_dictionary_ci
           =rtrim(cpc.customer_code,' ') COLLATE public.erp_dictionary_ci))
    WHERE cpc.is_deleted=FALSE
), supplier_matches AS (
    SELECT cpc.uid AS config_id,s.uid,s.supplier_code,count(*) OVER(PARTITION BY cpc.uid) AS matches
    FROM public.customer_po_config cpc JOIN public.supplier s
      ON COALESCE(s.is_deleted,FALSE)=FALSE AND s.uid<>'00000000-0000-0000-0000-000000000000'::uuid
     AND ((cpc.supplier_id IS NOT NULL AND s.uid=cpc.supplier_id)
       OR (cpc.supplier_id IS NULL AND rtrim(s.supplier_code,' ') COLLATE public.erp_dictionary_ci
           =rtrim(cpc.supplier_code,' ') COLLATE public.erp_dictionary_ci))
    WHERE cpc.is_deleted=FALSE
)
SELECT cpc.uid,c.uid AS customer_id,c.customer_code,s.uid AS supplier_id,s.supplier_code
FROM public.customer_po_config cpc
LEFT JOIN customer_matches c ON c.config_id=cpc.uid AND c.matches=1
LEFT JOIN supplier_matches s ON s.config_id=cpc.uid AND s.matches=1
WHERE cpc.is_deleted=FALSE;

DO $$
BEGIN
    IF EXISTS(SELECT 1 FROM customer_po_config_party_plan WHERE customer_id IS NULL OR supplier_id IS NULL) THEN
        RAISE EXCEPTION '存在空、无效、已删除或无法唯一回填的双方身份；请人工核实，不自动删除、合并或改绑';
    END IF;
    IF EXISTS(SELECT 1 FROM customer_po_config_party_plan GROUP BY customer_id,supplier_id HAVING count(*)>1) THEN
        RAISE EXCEPTION '存在同一客户 ID + 供应商 ID 的多份未删除配置；请人工确认保留策略，迁移不删数据';
    END IF;
    IF EXISTS(SELECT 1 FROM public.customer_po_config WHERE default_discount_rate<0
        OR default_discount_rate>999.999999 OR default_discount_rate<>round(default_discount_rate,6)) THEN
        RAISE EXCEPTION '折扣率超出 numeric(9,6) 范围或精度，拒绝隐式舍入、截断';
    END IF;
END $$;

-- 所有数据检查先于首次持久修改；移除编码唯一约束后，编码仅作兼容快照，不承担身份。
DROP INDEX IF EXISTS public.ux_customer_po_config_codes;
UPDATE public.customer_po_config cpc SET customer_id=p.customer_id,supplier_id=p.supplier_id,
    customer_code=p.customer_code,supplier_code=p.supplier_code,version=cpc.version+1,updated_date=localtimestamp
FROM customer_po_config_party_plan p WHERE cpc.uid=p.uid
  AND (cpc.customer_id IS DISTINCT FROM p.customer_id OR cpc.supplier_id IS DISTINCT FROM p.supplier_id
    OR cpc.customer_code IS DISTINCT FROM p.customer_code OR cpc.supplier_code IS DISTINCT FROM p.supplier_code);
ALTER TABLE public.customer_po_config ALTER COLUMN default_discount_rate TYPE numeric(9,6);
ALTER TABLE public.customer_po_config ADD CONSTRAINT ck_customer_po_config_parties CHECK (
    is_deleted=TRUE OR (customer_id IS NOT NULL AND customer_id<>'00000000-0000-0000-0000-000000000000'::uuid
        AND supplier_id IS NOT NULL AND supplier_id<>'00000000-0000-0000-0000-000000000000'::uuid));
CREATE UNIQUE INDEX ux_customer_po_config_parties ON public.customer_po_config(customer_id,supplier_id) WHERE is_deleted=FALSE;
COMMIT;
