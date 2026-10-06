# 采购与BOM的当前边界

核对日期：2026-10-06。本文整篇替换旧阶段计划，描述当前调用链及边界，不提供自动重构或开发授权。

当前共享BOM按BomNo读取、批量保存即维护当前结构；BomVersion只作有效标记。物料建立取得一致组成并保留拆解修订和采用依据。

ProcurementConfirmationBuilder只读取实际采购、产品/单位和活动来源贡献。确认前核对金额、实际供应商及ContextFingerprint，记录确认批次，不读当前BOM或创建新PurchaseConfirmationNode/Line。历史实体仍保留，不能作为现行写链证明。

PackingList标准执行依据来自活动NetDemand根/直接客供子件及PO Link；不按旧“采购确认时冻结全部BOM”的提示词重复开发。

依据：[确认](../../../SwiftExport.Application/Services/Procurement/PurchaseOrders/Confirmation/ProcurementOrderConfirmationService.cs)、[实际预览](../../../SwiftExport.Application/Services/Procurement/PurchaseOrders/Confirmation/ProcurementConfirmationBuilder.cs)、[BOM地图](../模块能力地图/BOM模块能力地图.md)、[采购地图](../模块能力地图/采购模块能力地图.md)。
