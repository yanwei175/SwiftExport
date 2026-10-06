# Excel采购导入检查与正式创建

核对日期：2026-10-06。POST api/for-excel/purchase-orders/check接收CheckPurchaseOrderImportRequest，返回ServiceResult<CheckPurchaseOrderImportResponse>；ForExcelPurchaseOrderService只检查客户端已解析数据，不读取Excel或写采购。

CanSave/Issues说明业务行检查，成功查询不表示已创建。正式独立采购创建调用采购领域草稿用例，仍核对实际客户/供应商、产品或非标身份、单位、数量及未关联量。后端独立采购与需求驱动草稿是不同输入路径，不为Excel导入伪造需求关系。

依据：[Controller](../SwiftExport.WebApi/Controllers/ForExcel/ForExcelPurchaseOrderController.cs)、[检查服务](../SwiftExport.Application/Services/ForExcel/ForExcelPurchaseOrderService.cs)、[DTO](../SwiftExport.Contracts/DTOs/ForExcel/PurchaseOrders)、[草稿规则](../SwiftExport.Application/Services/Procurement/PurchaseOrders/Draft/IndependentPurchaseOrderInputPolicy.cs)、[采购说明](采购用例说明.md)。未运行Excel或采购写命令。
