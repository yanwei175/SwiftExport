# PackingList 接口与源码索引

核对日期：2026-10-03。这里只记录现有接口。

## 公共接口

基址：`/api/shipping/packing-lists`。均支持 `CancellationToken`，响应外层为 `ServiceResult<T>`。

| 方法与相对路由 | 请求 | 响应 T |
|---|---|---|
| POST /search | PackingListManagerSearchRequest | PagedResponseBase<PackingListManagerListItemDto> |
| GET /{uid} | 无 | PackingListDetailDto |
| POST /sources/search | PackingListSourceSearchRequest | PagedResponseBase<PackingListSourceCandidateDto> |
| POST /drafts | CreatePackingListDraftCommand | PackingListDraftCommandResult |
| PUT /drafts/{uid} | UpdatePackingListDraftCommand | PackingListDraftCommandResult |
| POST /drafts/{uid}/delete | DeletePackingListDraftCommand | PackingListDraftCommandResult |
| POST /drafts/{uid}/lines/{lineUid}/associate-sources | AssociatePackingListSourcesCommand | PackingListDraftCommandResult（含完整详情） |
| POST /{uid}/post-preview | PackingListPostPreviewRequest | PackingListPostingPreviewDto |
| POST /{uid}/post | PostPackingListCommand | PackingListPostingCommandResult |
| POST /{uid}/reverse-preview | PackingListReversePreviewRequest | PackingListPostingPreviewDto |
| POST /{uid}/reverse | ReversePackingListCommand | PackingListPostingCommandResult |
| GET /{uid}/trace | 无 | PackingListTraceDto |

独立 ForExcel 入口：POST `/api/for-excel/packing-lists/check`，请求 `CheckPackingListImportRequest`，返回 `CheckPackingListImportResponse`；只读检查不替代保存复核。当前没有第三阶段纠错、负库存例外或差异关闭接口；也不将旧计划中的 export 路由列为已实现。

## 调用约定

- 预览携带期望版本、库存业务日期；反向另需原因。库存业务日期独立于 InvoiceDate。
- 正式命令携带稳定 CommandId、预览令牌；客户端不指定计算结果或操作人。结果不确定时保留原命令重试，不换 ID。
- 业务结果区分成功、重复成功、版本冲突、预览失效和阻断；应同时处理外层失败和内部业务状态，不能仅因 HTTP 成功就刷新为已过账。
- 过账和反向分别使用独立权限；不借用手工入出库权限。正常操作仍禁止负库存。
- 来源查询支持批量关键词，返回 ImagePath；草稿本地已选量由前端扣除，服务端正式交付净额与分页过滤仍由后端负责。
- 来源查询支持 ProductId、IsNonStandard 和 InvoiceNumbers 精确集合。来源 InvoiceNo 联查销售单发票号或系统售后号，不以 PO 查询。活动、同客户和正式余额为草稿候选准入，采购执行条件保留在过账。
- 待关联草稿允许空 GrossDemandId；分配来源需稳定 CommandId、头行版本及每个来源版本、显式数量，部分分配保留待关联余量。待关联行不可过账。
- 两端专项验收及部署说明见 [导入与来源关联实施说明](../../../../docs/PackingList导入与来源关联实施说明.md)。

## 源码入口

- [Refit：IPackingListApi](../../../../SwiftExport.Contracts/Apis/Shipping/IPackingListApi.cs)
- [Controller：PackingListController](../../../../SwiftExport.WebApi/Controllers/Shipping/PackingLists/PackingListController.cs)
- [DTO 目录](../../../../SwiftExport.Contracts/DTOs/Shipping/PackingLists)
- [草稿服务：PackingListDraftService](../../../../SwiftExport.Application/Services/Shipping/PackingLists/PackingListDraftService.cs)
- [过账服务：PackingListPostingService](../../../../SwiftExport.Application/Services/Shipping/PackingListPostingService.cs)
- [事务协调器：PackingListPostingCoordinator](../../../../SwiftExport.Application/Services/Shipping/PackingListPostingCoordinator.cs)
- [执行追踪：PackingListTraceService](../../../../SwiftExport.Application/Services/Shipping/PackingListTraceService.cs)
- [自动入出库内部接口](../../../../SwiftExport.Application/Interfaces/Inventory/IAutomaticPostingPorts.cs)
- [WPF ViewModel 目录](../PackingLists/ViewModels)
- [WPF View 目录](../PackingLists/Views)

内部入出库 Port 不是 HTTP API：由 PackingList 协调器在同一 DbSession 事务中调用；不得用 HTTP 自调用代替同事务编排。详细约束见 [第一阶段交接](PackingList自动入出库内部接口交接.md)。
