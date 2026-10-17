# BOM模块能力地图

核对日期：2026-10-06。本文描述当前工作树实现，不沿用旧阶段计划或以历史测试代替当前验收。业务判断以所列服务、实体和实际调用方为准；新增业务规则须另行确认。全部 HTTP 入口见[业务 API 索引](../../../docs/业务API索引.md)，跨域主线见[业务总纲](../业务架构总纲.md)。

## 职责与事实

BOM 是单套当前共享组成，按 BomNo 组织，多个产品可绑定同一组成。BomVersion 是是否需要 BOM 的兼容标记，不能当可选历史业务版本：0 不需要，正数需要；正数且 BomNo 为空表示待建立或绑定，正数且编号非空表示已有绑定，组成是否为空独立判断。保存即维护当前结构；物料拆解 DecompositionRevision 和箱单执行证据是采用时历史，不是 BOM 发布版本。

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

当前不提供版本发布、历史版本选择或 BomVersion 切换用例；保存结果冲突或事务结果未知须按原返回语义处理，不能用覆盖或重试掩盖。父件资格按 2026-10-17 确认口径统一：只有成品可以拥有 BOM，生产、采购、外协均允许；零件、虚拟件、客供件、包材只能作为子件。成品作为子件不新增限制，客供件仍须符合采购型终端物料约束。BomParentPolicy 负责通用父件资格及需求标记/编号合法性，产品新增、修改、详情保存、专用绑定及共享组成保存均复用；共享组成须检查全部实际父件。CustomerSuppliedMaterialPolicy 保留客供件专属约束。

管理页分页固定查询未删除、BomVersion > 0 的成品，包含已标记需要但尚未绑定的成品，不增加供货方式或启用状态过滤；总数与页数据采用相同条件。共享人数保留全部真实引用者，不以成品条件隐藏历史异常绑定。历史非法绑定不自动清理，触碰相应保存边界时拒绝并提示核实。

物料列表与详情以“需要 BOM”勾选表达需求。未绑定时允许取消为 0；已有绑定不允许直接取消、清编号或换组，包括尚未录入组成的绑定。专用建立/绑定要求先保存正需求标记，并保留已有正数。详情通过可选 RequiresBom 传递意图：省略/null 保持，true 保留正数或设为 1，false 申请归零并接受绑定保护；详情不开放 BomNo 写入。0 配非空编号及负标记仍拒绝，不自动清理历史数据。

## 源码入口

- [BomService](../../../SwiftExport.Application/Services/Products/BomService.cs)
- [产品绑定边界](../../../SwiftExport.Application/Services/Products/Catalog/Maintenance/ProductMaintenanceService.SaveBoundary.cs)
- [实体](../../../SwiftExport.Core/Entities/MasterData/Bom.cs)
- [API](../../../SwiftExport.Contracts/Apis/ProductApis/IBomApi.cs)
- [WPF](../../../SwiftExport.Client.Wpf/Features/Products/Bom)
- [实际拆解](../../../SwiftExport.Application/Services/DemandPlanning/Materials/Establishment/GrossDemandDecompositionPlanner.cs)


## 使用与验证边界

本文的“已实现”表示源码及接线存在，不表示本次运行了业务测试、访问了生产数据库或完成了客户端人工验收。修改相关能力时，按真实消费者选择必要构建和测试；不得根据本文恢复旧接口、重复执行旧重构计划或自动扩大任务。
