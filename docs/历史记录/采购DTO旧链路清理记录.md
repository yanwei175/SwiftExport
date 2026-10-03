> 历史验收记录：仅表示标题日期的实现与验证结果，不是当前执行规范。当前规则从根 AGENTS.md 与工程架构总纲进入；旧快照前置、迁移编号和测试状态不得直接套用。

# 采购 DTO 旧链路清理（2026-09-30）

## 当前详情链

`IProcurementOrderApi.GetDetailAsync → ProcurementOrderQueryService → ProcurementOrderQueryRepository` 返回 `PurchaseOrderDetailDto`（命名空间 `SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderDetailDto`）。WPF 通过 `PurchaseOrderHeaderEditorModel.Load` 和当前行集合接收，不再保留旧详情装载入口。

价格倍率 `DiscountRate`、整单优惠 `DiscountAmount` 原已存在于详情 SQL 和 DTO，但头模型及详情 XAML 遗漏。本次补齐只读展示，保留空值和零值，不改变行价、总额或草稿保存白名单。创建采购审核页继续使用其自身预览契约。

## 已清理

- 未被实际接口消费的 `ProcurementOrderDetailDto`、`ProcurementOrderLineDto`、`ProcurementOrderProgressSummaryDto`。
- 无调用方的 `SaveProcurementOrderCommand`、`SaveProcurementOrderLineCommand`。
- 旧 `PurchaseOrderWorkspaceDtos.cs` 聚合契约与头模型旧装载方法。
- 无调用方的 `ProcurementOrderLineEditModel`、`PurchaseOrderLineEditorModel`。
- 详情 SQL 中没有对应返回属性的 `OriginKind`、`ExecutionRequestId` 投影；实体字段不删除。

旧聚合文件中的 `BusinessOperationTraceItemDto` 仍被通用审计仓储和测试使用，移到同命名空间的独立文件，保持字段不变。现用查询、创建预览、确认、反向、跟进与 Trace 契约保留，不因字段相似合并不同用例。当前 Trace 已有实际服务、仓储、接口及页面调用，不能依据早期“占位页”记录删除。

## 验证

- 后端解决方案与 WPF 隔离输出构建通过。
- 后端采购快照及能力契约测试 9 项通过。
- WPF 头模型快照、行集合、现用装载入口相关测试 5 项通过（隔离测试项目）。
- 完整 WPF 测试项目未通过编译：来源及物料需求两处测试替身未实现新增的 `GetTraceAsync`，属于本次范围外。
- 未进行桌面联验，未修改业务数据库。
