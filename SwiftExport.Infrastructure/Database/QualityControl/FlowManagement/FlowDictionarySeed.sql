-- 品质管理／流量管理字典初始化。允许显式重复执行，不清库，不改写既有名称或启用状态。
-- 仅初始化 FlowTestFunction/Default 和 FlowLimiterSpecification/不限流，不授予角色权限。
BEGIN;
-- 两张公共字典表短时串行化初始化，防止重复执行并发插入相同明细；锁仅限本初始化事务。
LOCK TABLE public.sys_dict_type, public.sys_dict_data IN SHARE ROW EXCLUSIVE MODE;

INSERT INTO public.sys_dict_type
    (uid,version,is_deleted,created_date,created_by,updated_date,updated_by,
     dict_code,dict_name,is_system,remark,parent_id,sort_order,group_code,group_name)
SELECT gen_random_uuid(),1,FALSE,clock_timestamp() AT TIME ZONE 'UTC','FlowSeed',
       clock_timestamp() AT TIME ZONE 'UTC','FlowSeed',v.code,v.name,TRUE,
       '品质管理流量测试使用的公共字典',NULL,v.sort_no,'QualityControl','品质管理'
FROM (VALUES ('FlowTestFunction','流量测试功能',10),
             ('FlowLimiterSpecification','限流器规格',20)) AS v(code,name,sort_no)
WHERE NOT EXISTS (
    SELECT 1 FROM public.sys_dict_type t
    WHERE t.is_deleted=FALSE AND rtrim(t.dict_code,' ')=v.code
);

INSERT INTO public.sys_dict_data
    (uid,version,is_deleted,created_date,created_by,updated_date,updated_by,
     dict_code,data_name_en,data_name_zh,remark,sort_order,css_class,is_system,is_enabled)
SELECT gen_random_uuid(),1,FALSE,clock_timestamp() AT TIME ZONE 'UTC','FlowSeed',
       clock_timestamp() AT TIME ZONE 'UTC','FlowSeed',v.code,v.name_en,v.name_zh,
       v.remark,0,NULL,TRUE,TRUE
FROM (VALUES ('FlowTestFunction','Default','Default','默认测试功能'),
             ('FlowLimiterSpecification','Unlimited','不限流','本功能未安装限流器'))
     AS v(code,name_en,name_zh,remark)
WHERE EXISTS (
    SELECT 1 FROM public.sys_dict_type t
    WHERE t.is_deleted=FALSE AND rtrim(t.dict_code,' ')=v.code
)
AND NOT EXISTS (
    SELECT 1 FROM public.sys_dict_data d
    WHERE d.is_deleted=FALSE AND rtrim(d.dict_code,' ')=v.code
      AND (rtrim(d.data_name_en,' ')=v.name_en OR rtrim(d.data_name_zh,' ')=v.name_zh)
);
COMMIT;
