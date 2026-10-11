# Excel采购导入检查与正式创建

核对日期：2026-10-11。POST api/for-excel/purchase-orders/check 接收 CheckPurchaseOrderImportRequest，返回 ServiceResult<CheckPurchaseOrderImportResponse>；ForExcelPurchaseOrderService 只检查客户端已解析数据，不读取 Excel 或写采购。

CanSave/Issues说明业务行检查，成功查询不表示已创建。正式独立采购创建调用采购领域草稿用例，仍核对实际客户/供应商、产品或非标身份、单位、数量及未关联量。后端独立采购与需求驱动草稿是不同输入路径，不为Excel导入伪造需求关系。

检查会同时返回单头问题与产品匹配结果。客户端可在尚未选择供应商或单头未完整时单独匹配 SKU，仅接纳行产品结果，不能把这种响应中的 CanSave 当作保存资格。标准行仍通过客户代码＋客户型号确认客户产品关系；候选和 LinkSku 差异需明确选择或确认后重新检验，非标须用户明确指定。优惠和价格继续是快照记录，采购币种固定 CNY。

非空 BONO 在全部未软删除采购订单中唯一，跨客户、供应商和生命周期状态。检查新增 BoNoAlreadyExists 问题，字段 BoNo，重复时 CanSave=false，不允许确认绕过。独立采购创建与需求转采购的新建分支都在事务中重新查重；并发重复由 ux_purchase_order_head_bo_no 部分唯一索引拒绝。两条头插入路径返回 RepositoryWriteResult，调用方检查失败并回滚；只将此索引的唯一冲突归类为明确 VALIDATION，其他约束异常沿原分类。

统一提示：“自定义采购单号（BONO）已被未删除的采购订单使用，请修改后重新保存。”幂等重放先返回已成立结果，不重复占号；显式追加既有草稿不创建新头、不按新号查重。普通草稿更新仍不能修改 BONO。

VSTO 默认配置复用现有 ICustomerPoConfigApi.GetByPartiesAsync，需 Customers.PoConfig.View，前端只补空值；无配置或失败可以手工填写，不新增配置缓存或 HTTP 保存链路。升级脚本和验证边界见 [采购 BONO 唯一限制与升级说明](采购BONO唯一限制与升级说明.md)。

依据：[Controller](../SwiftExport.WebApi/Controllers/ForExcel/ForExcelPurchaseOrderController.cs)、[检查服务](../SwiftExport.Application/Services/ForExcel/ForExcelPurchaseOrderService.cs)、[DTO](../SwiftExport.Contracts/DTOs/ForExcel/PurchaseOrders)、[草稿规则](../SwiftExport.Application/Services/Procurement/PurchaseOrders/Draft/IndependentPurchaseOrderInputPolicy.cs)、[采购说明](采购用例说明.md)。本轮构建和纯托管用例测试已执行；未运行 Excel 实机、数据库迁移或实际采购写命令。
