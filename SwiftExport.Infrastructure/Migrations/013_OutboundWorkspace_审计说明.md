# 出库工作台 Phase 0 审计说明

## 目的与边界

`013_OutboundWorkspace_Preflight.sql` 只读取遗留 `OutboundHead/Line`、`PackingListHead/Line`、库存流水、库存余额和入库表的结构与数据质量。它不会执行 DDL、DML、回填、修复或连接配置变更；实际在共享数据库运行前仍需用户当次授权。

本阶段不新增 `OutboundDocument`、Contracts、API、Repository、Application Service 或 WPF 页面，也不得恢复已清理的旧出库 C# 链路。

## 可证明性的判定

只有满足以下全部条件的行，才可进入“可证明遗留事实候选”清单：

1. 出库行存在明确的 `PackingListContributionId` 或 `NetDemandAllocationId`；
2. 该 Uid 在相应上游表中确实存在且处于可追溯范围；
3. 可继续追溯到销售来源，且库存流水与该出库行的单据/行 Uid 一致。

单号、SKU、`BizType`、`BizNo`、`RefNo`、供应商编码或相同数量只能作为人工核查线索，不能单独证明关系。缺少上述 Uid 证据的记录必须列入“不可证明来源”清单：保留为遗留只读事实，不回填、不纳入新最终客户已发货汇总、不支持新命令或自动反过账。

## 审计交付清单

执行预检后，按下表归档结果；本仓库不保存任何共享库数据导出。

| 清单 | 最少字段 | 处理结论 |
| --- | --- | --- |
| 可证明遗留事实候选 | Outbound 头/行 Uid、PackingListContributionId 或 AllocationId、销售来源、库存流水 Uid | Phase 1 前逐条复核；仅可作为后续只读展示候选。 |
| 不可证明来源 | Outbound 头/行 Uid、现有单号/SKU、缺失证据说明 | 保留原状并从新命令、反过账、已发货净额中排除。 |
| 表结构与字段缺口 | 物理表、现有列、目标字段、候选列、结论 | 用于 Phase 1 最小增量扩字段设计。 |
| 关系与库存风险 | 孤立行、无明细头、重复单号、非零/重复余额、流水信号 | 作为迁移前阻断或人工确认项，禁止自动修复。 |

## Phase 1 输入门槛

- 已人工确认 `OutboundHead/Line`、`PackingListHead/Line` 的真实父子键和可用字段。
- 已生成并确认上述两份来源清单；没有证据的遗留记录保持空关联。
- 已确认非零库存、重复余额和缺失流水的处理责任；Phase 1 不得借机修改它们。
- 仅在以上事实明确后，才设计 `OutboundDocument/Line` 对既有物理表的最小映射与 `013` 后续增量结构。

## 已冻结的后续接口边界

- 内部 `IOutboundWorkspaceService` 负责正常出库、反过账和 PackingList 编排；不提供普通手工客户发货。
- 公共 API/Refit 仅规划查询、详情、Trace、负库存异常预览/提交和反过账预览。
- 正常出库只能调用 `IInventoryPostingService`，每行以 `CommandItemId` 保证幂等。
- 负库存异常在具备 `Inventory.NegativeStock.Override` 的细粒度权限判定前保持关闭。
