-- 仅供 192.168.3.12:5432/dv_purchase_draft_lifecycle_tests_20261020 新建专库。
-- 先由受门禁测试入口核对数据库不存在，再执行当前 Generated/SwiftExport.PostgreSql.Schema.preview.sql。
-- 本文件不建库、不删除或重置任何 schema，不导入业务库数据；所有测试事实保留供复核。
DO $$ BEGIN
    IF current_database()<>'dv_purchase_draft_lifecycle_tests_20261020' THEN
        RAISE EXCEPTION '采购草稿专库名称不匹配，拒绝初始化';
    END IF;
    IF EXISTS(SELECT 1 FROM public.sys_user) THEN RAISE EXCEPTION '专库已有种子，拒绝重复初始化'; END IF;
END $$;

-- 使用真实非管理员角色与显式权限关系，不能用SYSTEM_ADMIN绕开细权限路径。
INSERT INTO public.sys_role(uid,role_code,role_name,is_active,is_system)
VALUES('20261020-0000-0000-0000-000000000001','PURCHASE_DRAFT_TEST','采购草稿定向验收',TRUE,FALSE);
INSERT INTO public.sys_user(uid,user_name,password_hash,credential_version,is_active)
VALUES('20261020-0000-0000-0000-000000000002','purchase-draft-test','test-only-no-login',1,TRUE),
      ('20261020-0000-0000-0000-000000000003','purchase-draft-denied','test-only-no-login',1,TRUE);
INSERT INTO public.sys_user_role(uid,user_id,role_id)
VALUES(gen_random_uuid(),'20261020-0000-0000-0000-000000000002','20261020-0000-0000-0000-000000000001');
-- 权限目录由测试入口依据PermissionCatalog写入本专库，并只将本轮所需键分配给上述角色。
INSERT INTO public.customer(uid,customer_code,customer_name,doc_relate_no,deposit_rate,lead_time_days,is_active)
VALUES('20261020-0000-0000-0000-000000000010','PDT-C','验收客户','PDT-C',0,0,TRUE);
INSERT INTO public.supplier(uid,supplier_code,supplier_name,doc_relate_no,is_active)
VALUES('20261020-0000-0000-0000-000000000011','PDT-S','验收供应商','PDT-S',TRUE),
      ('20261020-0000-0000-0000-000000000012','PDT-S2','第二供应商','PDT-S2',TRUE);
INSERT INTO public.product(uid,sku,description_zh,unit,material_type,supply_type,bom_version,color_id,is_active)
VALUES('20261020-0000-0000-0000-000000000020','PDT-FG','验收成品','PCS','成品','采购',0,'00000000-0000-0000-0000-000000000000',TRUE);
INSERT INTO public.sys_dict_data(uid,dict_code,data_name_en,is_system,is_enabled)
VALUES(gen_random_uuid(),'Unit','PCS',TRUE,TRUE);
-- 每个场景另建独立MD成品需求及NetDemand身份，避免失败重跑覆盖成功事实。
