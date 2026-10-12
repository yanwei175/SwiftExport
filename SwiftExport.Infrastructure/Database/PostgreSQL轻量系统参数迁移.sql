-- 标准 PostgreSQL SQL，可在数据库编辑器或 psql 中执行，不依赖 psql 变量替换。
-- 下方目标按用户确认填写为 dv_next / 192.168.3.12 / 5432；其他目标须显式修改三项值，仍强制核对连接目标。
-- 使用独立会话一次执行整个文件；若上次执行已导致事务失败，先单独执行 ROLLBACK。
-- 不读取运行配置、不初始化值、不修改产品图片字段。重复执行会因表已存在而失败。
BEGIN;
SELECT set_config('swiftexport.expected_database', 'dv_next', true);
SELECT set_config('swiftexport.expected_server', '192.168.3.12', true);
SELECT set_config('swiftexport.expected_port', '5432', true);
DO $$
BEGIN
    IF current_setting('swiftexport.expected_database') = '请填写目标数据库名'
       OR current_setting('swiftexport.expected_server') = '请填写目标服务器IP'
       OR current_setting('swiftexport.expected_port') = '请填写目标端口' THEN
        RAISE EXCEPTION '请先填写迁移脚本顶部的目标数据库名、服务器 IP 和端口';
    END IF;
    -- inet 强转 text 会保留 /32 等掩码，使用 host 只比较服务器 IP，避免同一地址被误判。
    IF current_database() <> current_setting('swiftexport.expected_database')
       OR host(inet_server_addr()) IS DISTINCT FROM current_setting('swiftexport.expected_server')
       OR current_setting('port') <> current_setting('swiftexport.expected_port') THEN
        RAISE EXCEPTION '目标实例或库名不符，拒绝系统参数迁移。实际：数据库=%，服务器IP=%，端口=%；预期：数据库=%，服务器IP=%，端口=%',
            current_database(), host(inet_server_addr()), current_setting('port'),
            current_setting('swiftexport.expected_database'), current_setting('swiftexport.expected_server'), current_setting('swiftexport.expected_port');
    END IF;
END $$;

-- 与 SystemParameter 实体及 SchemaGenerator 预览一致；不对已有表执行不明结构的 IF NOT EXISTS。
CREATE TABLE public.system_parameter (
    uid uuid NOT NULL,
    version integer NOT NULL DEFAULT 1,
    is_deleted boolean NOT NULL DEFAULT FALSE,
    created_date timestamp(6) without time zone NULL,
    created_by varchar(500) NULL,
    updated_date timestamp(6) without time zone NULL,
    updated_by varchar(500) NULL,
    parameter_key varchar(100) NOT NULL,
    parameter_value varchar(2048) NOT NULL,
    CONSTRAINT pk_system_parameter PRIMARY KEY (uid),
    CONSTRAINT ck_system_parameter_value CHECK (length(btrim(parameter_value)) > 0),
    CONSTRAINT ck_system_parameter_version CHECK (version >= 1)
);
CREATE UNIQUE INDEX ux_system_parameter_key ON public.system_parameter (parameter_key);
COMMENT ON TABLE public.system_parameter IS '全局系统参数值与版本；定义由代码注册，不提供删除或更名入口。';
COMMENT ON COLUMN public.system_parameter.parameter_key IS '不可变唯一参数键';
COMMENT ON COLUMN public.system_parameter.parameter_value IS '应用层校验后的参数值';
COMMIT;
