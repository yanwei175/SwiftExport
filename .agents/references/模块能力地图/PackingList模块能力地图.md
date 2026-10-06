# PackingList模块能力地图

核对日期：2026-10-06。本文描述当前工作树实现，不沿用旧阶段计划或以历史测试代替当前验收。业务判断以所列服务、实体和实际调用方为准；新增业务规则须另行确认。全部 HTTP 入口见[业务 API 索引](../../../docs/业务API索引.md)，跨域主线见[业务总纲](../业务架构总纲.md)。

## 职责与事实

PackingListHead、PackingListLine 保存箱单及对 GrossDemand 的交付要求；PackingListPostingExecution/Line 保存正式执行依据。箱单生命周期 Active/Closed/Voided 与 Unposted/Posted/Reversed 独立。正式交付由未反向的已过账箱单计算，草稿不能计为已发。

## 当前能力

| 能力 | 服务 | 契约/页面 |
| --- | --- | --- |
| Manager、详情、来源 Picker | PackingListQueryService | IPackingListApi；PackingListManager、Detail、SourcePicker |
| 草稿保存、导入、来源关联、作废 | PackingListDraftService、PackingListImportChecker | 真实来源数量分配；未完全关联不得正式过账 |
| 过账预览/正式过账 | PackingListPostingService | 预览动作/阻断及令牌；正式命令复核来源、采购容量和库存资源 |
| 反向预览/整单反向 | PackingListReversalService | 使用执行证据反向全部相关自动单据，不读当前 BOM 重算历史 |
| Trace | PackingListTraceService | 只读来源、执行和自动单据追溯 |
| Excel 检查 | ForExcelPackingListService | 检查入口与正式写入分离 |

## 实际过账依据

PackingListPostingFactsResolver 的当前标准路径读取来源的唯一活动根 NetDemand、同修订子件和根上的 PO Link。客供件用量来自拆解 GrossQty、父子及送达关系；不是采购确认新建 BOM 节点或现时 BOM。标准产品必须证明来源依据、产品单位及真实采购归属，结合供应商虚拟仓库存和入库模块提供的采购净可收容量安排自动收货、最终出库和客供件消耗。

当前 deliveryOnly 条件是 IsNonStandard **或** SourceType=CR。这些行产生正式交付而不产生库存动作；不能沿用“所有标准补发都自动入出库”的旧方案。仓库选择和库存不足等阻断以预览生成器及真实命令复核为准。

正式过账/反向由箱单最外层事务统一提交来源额度、执行证据、自动单据、流水、状态和回执。Preview 不预留；令牌过期、并发版本、来源总额度或采购容量改变时应重读，不在前端拼接库存事实。当前公开 API 为整单过账/反向，没有通用已过账修订或多次替换发货命令。

## 源码入口

- [服务与预览计算](../../../SwiftExport.Application/Services/Shipping/PackingLists)
- [实际依据解析](../../../SwiftExport.Application/Services/Shipping/PackingLists/PackingListPostingFactsResolver.cs)
- [Controller](../../../SwiftExport.WebApi/Controllers/Shipping/PackingLists/PackingListController.cs)
- [实体](../../../SwiftExport.Core/Entities/Shipping)
- [API](../../../SwiftExport.Contracts/Apis/Shipping/IPackingListApi.cs)
- [WPF](../../../SwiftExport.Client.Wpf/Features/Shipping/PackingLists)


## 使用与验证边界

本文的“已实现”表示源码及接线存在，不表示本次运行了业务测试、访问了生产数据库或完成了客户端人工验收。修改相关能力时，按真实消费者选择必要构建和测试；不得根据本文恢复旧接口、重复执行旧重构计划或自动扩大任务。
