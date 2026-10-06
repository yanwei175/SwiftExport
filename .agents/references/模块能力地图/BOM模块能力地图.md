# BOM模块能力地图

核对日期：2026-10-06。本文描述当前工作树实现，不沿用旧阶段计划或以历史测试代替当前验收。业务判断以所列服务、实体和实际调用方为准；新增业务规则须另行确认。全部 HTTP 入口见[业务 API 索引](../../../docs/业务API索引.md)，跨域主线见[业务总纲](../业务架构总纲.md)。

## 职责与事实

BOM 是单套当前共享组成，按 BomNo 组织，多个产品可绑定同一组成。BomVersion 是有效性标记，不能当可选历史业务版本。保存即维护当前结构；物料拆解 DecompositionRevision 和箱单执行证据是采用时历史，不是 BOM 发布版本。

## 当前能力

| 能力 | 实现/消费者 | 边界 |
| --- | --- | --- |
| 编号组成读取、列表/分页 | BomService、IBomApi | 查询按实际共享编号获取，不要求组成独占一个产品 |
| 校验、树读取 | BomService、共享结构校验器 | 验证产品、组成、循环等现有规则；树不等于物料计划 |
| 批量保存 | BomService.BatchSaveAsync | Serializable 事务、版本及共享图校验；不自动重试结果不明提交 |
| 产品绑定保存 | ProductMaintenanceService.SaveBoundary | 产品与共享组成同一边界复核，禁止形成损坏绑定 |
| 客户端组成维护 | Products/Bom 与抽屉 | 本地编辑模型、批量请求，服务端校验权威 |
| 实际采用 | MaterialDemandEstablishmentService | 读取一致结构形成根及直接客供件物料历史，已有事实不跟随当前编辑重算 |

## 实际规则

GrossDemandDecompositionPlanner 当前建立根及直接客供子件，不是任意层级全量 MRP。采购确认不读取 BOM、不创建新确认节点；箱单标准路径读取实际物料拆解和 PO Link。采购历史确认实体仍存在不能证明当前确认仍写它们。

当前不提供版本发布、历史版本选择或 BomVersion 切换用例；保存结果冲突或事务结果未知须按原返回语义处理，不能用覆盖或重试掩盖。组成和根产品的客供件/供货方式限制以 CustomerSuppliedMaterialPolicy 及共享结构验证器为准。

## 源码入口

- [BomService](../../../SwiftExport.Application/Services/Products/BomService.cs)
- [产品绑定边界](../../../SwiftExport.Application/Services/Products/Catalog/Maintenance/ProductMaintenanceService.SaveBoundary.cs)
- [实体](../../../SwiftExport.Core/Entities/MasterData/Bom.cs)
- [API](../../../SwiftExport.Contracts/Apis/ProductApis/IBomApi.cs)
- [WPF](../../../SwiftExport.Client.Wpf/Features/Products/Bom)
- [实际拆解](../../../SwiftExport.Application/Services/DemandPlanning/Materials/Establishment/GrossDemandDecompositionPlanner.cs)


## 使用与验证边界

本文的“已实现”表示源码及接线存在，不表示本次运行了业务测试、访问了生产数据库或完成了客户端人工验收。修改相关能力时，按真实消费者选择必要构建和测试；不得根据本文恢复旧接口、重复执行旧重构计划或自动扩大任务。
