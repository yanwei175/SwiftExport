# Excel销售导入检查

核对日期：2026-10-06。POST api/for-excel/sales-orders/check接收CheckSalesOrderImportRequest，返回ServiceResult<CheckSalesOrderImportResponse>，Controller调用ForExcelSalesOrderService.CheckAsync。

后端检查已解析数据，不读取工作簿、不保存或确认订单。逐行业务问题通过成功查询结果中的CanSave/Issues表达；请求或权限失败使用公共错误协议。实际草稿写入走销售草稿服务，不能把检查成功等同于已保存。

标准行采用真实产品及单位，非标要求和重复/候选规则按服务与DTO当前实现；客户端必须在输入改变后重新检查，不用旧结果提交不同数据。地址读取配置是独立ExcelReadProfile能力。

依据：[Controller](../SwiftExport.WebApi/Controllers/ForExcel/ForExcelSalesOrderController.cs)、[服务](../SwiftExport.Application/Services/ForExcel/ForExcelSalesOrderService.cs)、[DTO](../SwiftExport.Contracts/DTOs/ForExcel/SalesOrders)、[VSTO销售导入](../SwiftExport.Client.VSTO/docs/销售导入检验与草稿保存说明.md)。本轮未操作Excel或导入数据。
