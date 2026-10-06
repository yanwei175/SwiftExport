# PackingList与出库的当前边界

核对日期：2026-10-06。本文整篇替换旧阶段计划，描述当前调用链及边界，不提供自动重构或开发授权。

PackingListPostingService及PackingListReversalService已拥有正式过账/整单反向编排；AutomaticPostingPorts加入同一父事务，统一库存能力写余额流水。标准来源读取活动物料树、PO Link、供应商仓及入库领域净可收容量，预览与正式执行分别校验。

当前非标或CR来源为deliveryOnly，仅产生正式交付；标准售后补发不能因为ProductId存在就被文档描述为已自动入出库。标准来源的客供件数量来自采用时拆解事实，反向使用箱单执行证据，不读当前BOM重算。

最终客户出库和客供件消耗属于箱单自动链；人工StockIssue是普通库存出库，不是来源履约。当前API提供整单反向，没有通用已过账修订或实际退货业务。

依据：[实际依据解析](../../../SwiftExport.Application/Services/Shipping/PackingLists/PackingListPostingFactsResolver.cs)、[箱单地图](../模块能力地图/PackingList模块能力地图.md)、[出库地图](../模块能力地图/出库模块能力地图.md)。
