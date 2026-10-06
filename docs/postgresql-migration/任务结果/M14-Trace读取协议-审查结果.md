# M14-P03-REVIEW-01：两类 Trace 读取协议审查

## 结论与交付边界

两类 Trace 都是分阶段读取，不具备仓储自己建立的一致快照保证。建议 PostgreSQL 按源语句边界在同一既有连接上顺序执行独立命令，透传可空外层事务及取消令牌；用局部 Guid 集合和覆盖 DTO 集合替代两个只读临时表。Material 的覆盖量必须在一个 SQL 语句中计算并一次物化，随后采购展示复用该物化值，不能再查询库存并替换覆盖量。

源空头返回的后续批次执行、取消及临时表清理尚无实证，不能直接定为“后续必执行”或“后续必跳过”。这项及源实例实际隔离配置、真实并发时序需要主控冻结/验收，当前结论不代表 Trace 迁移通过。主控接回前不实施 Trace；A 保持 Material 工作台和后续两个 partial 的同一写入归属。

本轮只新增本结果及更新执行 B 状态。主仓库只读，未同步整个 worktree，未修改业务源码、测试、旧结果或主清单；按分派未运行构建、测试或数据库。未提交、推送、部署、DDL、开启代理或其他聊天。事实来源为主仓库 `D:/Documents/source/CSharp/SwiftExport` 当前源码；包为 Dapper 2.1.66、Npgsql 10.0.3。

## 源执行阶段与物化边界

源码定位：

- `SwiftExport.Infrastructure/Repositories/DemandPlanning/Sources/Query/GrossDemandQueryRepository.Trace.cs`：16–19 scope/Guard；20 起头查询；179 DROP；181–204 批次和五结果消费；205–225 原 C# 汇总。
- `SwiftExport.Infrastructure/Repositories/DemandPlanning/Materials/Query/MaterialDemandQueryRepository.Trace.cs`：16–19 scope/Guard；20–31 头；33–38 tree；41–104 覆盖物化；106–172 采购结果；173–174 DROP；177–203 批次和三结果消费/汇总。
- `SwiftExport.Infrastructure/Repositories/Inventory/Inbounds/InboundReceiptHistorySql.cs`：源 scoped Guard；同目录 `PostgreSqlInboundReceiptHistoryGuard.cs`：17 起 scoped 入口，25 起借用会话查询。
- Core 的 `IGrossDemandQueryRepository`、`IMaterialDemandQueryRepository` 以及对应 Application QueryService 的 `GetTraceAsync` 均未约定自建事务或单快照；服务没有为 Trace 开启事务。

以上路径均相对主仓库，未读取弃用资料作为事实来源。

| 阶段 | Gross | Material | 必须保留的时间含义 |
| --- | --- | --- | --- |
| 初始 scope | 该 Gross 的所有 NetDemand 所关联的 PO TargetLineId 去重 | 当前 NetDemand 关联的 PO TargetLineId 去重 | SELECT INTO 执行时一次物化；两者均没有 Link 状态/删除过滤，不可收紧到“有效承接” |
| 历史验证 | 使用初始 scope 检查入库历史 | 使用初始 scope 检查入库历史 | 先于头查询；不是全库检查，也不是覆盖计算 |
| 第一个结果 | 未删除 Gross 头，LEFT JOIN 未删除 Customer | 未删除 NetDemand 头，JOIN Gross，LEFT JOIN 未删除 Product | Material 头不因 Gross 已删除而自行消失；缺客户/历史状态由既有理由规则处理 |
| 第二个结果 | Materials，包含历史/删除 NetDemand | 同一 Gross 下未删除的所有兄弟 NetDemand、所有修订 | tree 实际按 Gross 筛选，不可据 DTO“同棵树”描述改成 Parent/Root 遍历 |
| 覆盖内部阶段 | 无 | SQL 一次计算并写入 #MaterialReceiptCoverage | **此时重新查询当前 NetDemand 的 Link 范围，不使用初始 #TracePurchaseLines** |
| 采购结果 | 第三个结果 Purchases | 第三个结果 Purchases | 当前采购/Link/进度/收货量来自该展示语句；Material 覆盖量来自先前临时集合 |
| 尾部结果 | 第四 Deliveries，第五非法装箱历史计数 | 无 | 不可合并进头/采购语句并宣称快照行为相同 |
| 释放 | DROP 初始 scope | DROP 覆盖、scope | 源正常批次尾部；空头/取消/异常时是否执行尚待实证 |

这里“时点”指数据库执行语句的边界，不是客户端调用 ReadAsync 的时刻。驱动可能预读或提前发送，不能据客户端等待位置断言源端每条语句何时开始。需要真实数据库屏障验证。

Material 初始 scope 与稍后的覆盖候选集合不能合并。若 Link 在其间变化，后者会看到不同范围；新出现的关联头不一定被先前 Guard 验证。这是源阶段关系中的缺口，迁移本轮不自行增加第二次 Guard、刷新 scope 或锁定 Link。是否收紧须主控另行决定业务行为。

## Material 覆盖量的精确边界

覆盖候选针对当时 Link 范围内的采购行，收集该行的**所有**有效状态、未删除、正承接量 PO Link，包括其他 NetDemand/Gross 的承接；不能只取当前需求的 6。采购头/行采用 INNER JOIN 的既有匹配关系，缺失目标被此内部候选排除，而展示采用 LEFT JOIN 保留缺目标及理由；不能用同一“资格助手”统一改写。

资格 IsValid 同时检查 NetDemand/Gross 状态与删除、客户一致、需求版本与指纹、MD/非 MD 来源版本、单位、采购状态等。候选中的无效项通过分区 InvalidCount 使覆盖未知，不是过滤掉无效项再分摊。总承接超过采购行量、采购量/收货量负数、收货单位异常也使覆盖为 NULL；NULL 表示未知，不等于零。

窗口按 TargetLineId 分区，按需求日期（NULL 最后）、来源行号（NULL 最后）、拆解行号、Link Uid 排序，用明确 ROWS 窗口得到 EarlierLinkedQty。SQL 将正式收货量减去此前承接，裁剪到当前承接量范围。6+6、正式收货 7 的基线必须为 6/1。并列顺序会影响覆盖归属，不能删除稳定尾键或用默认 RANGE。

临时覆盖表只保存 LinkId、TargetLineId、nullable Coverage。最终采购语句重新读取 PurchaseReceivedQty/收货单位，再从冻结覆盖表 SUM 已覆盖量和 COUNT NULL，用于 OutsideCurrentCoverageQty。这允许展示收货量与覆盖物化时点不同：收货 7 物化出 6/1 后，展示阶段若看到 8，仍保留 6/1，再按源 SQL 计算外围量；不能改成同步刷新覆盖，或把旧覆盖阶段的收货量一并冻结到展示。

局部 DTO 只是临时表内容的等价副本。IsValid、窗口优先级、Coverage 裁剪、分区总量、NULL 计数、外围量等仍在 SQL 计算。原有 C# DTO 汇总可以原样保留，不新增 C# 数量算法。

其他需要保持的投影边界：Material IsActiveLink 不是完整来源资格；相关采购理由使 associated 未知。associated 超过 NetQty 时，源先记录超量理由，但已经计算的 associated 保留，coverage 才为 NULL。Gross 的来源版本/指纹理由本身不使 DeliveredQty 变 NULL，装箱理由才阻断交付净额，源码测试对此已有有效断言；源码相邻注释不能代替实现。两类 Shared 标识的实际条件也不同，Gross 的 other Link 未限制 TargetType，而 Material 有 PO 条件，不在迁移中顺手统一。

## 最小 PG 协议建议

1. 从当前 DbSession 借用同一已打开连接和可空外层事务；不创建/关闭连接，不 Begin/Commit/Rollback，不改变隔离，不添加业务锁、数据库函数或泛用多库框架。会话与事务由外层拥有，调用期间不得并发替换 Transaction 或复用连接执行另一 reader。
2. 每个阶段一个独立、完整缓冲的 await 查询，上一阶段 reader 释放后才进入下一阶段。每个 CommandDefinition 透传同一取消令牌及外层事务。Guard 复用已完成的 PostgreSqlInboundReceiptHistoryGuard.ValidateScopedAsync，它现在从同一 Session 读取 Transaction；不得在 Trace 调用期间改变会话事务。
3. Gross 顺序：scope Guid[] → scoped Guard → header → Materials → Purchases → Deliveries → 非法装箱历史计数 → 原 DTO/理由/交付汇总。Material 顺序：scope Guid[] → scoped Guard → header → tree → 一条覆盖 SQL 及局部覆盖集合 → 一条采购 SQL（绑定冻结覆盖）→ 原 DTO 汇总。空头是否继续后续阶段，暂不定案，见下一节。
4. scope 使用 DISTINCT Guid[] 一次物化并传 Guard；历史/软删除 Link 不省略。空数组保持类型为 uuid[]，不把空 scope 解释为全库。现有 PG Guard 复制传入集合后做 ANY 检查，验证 scope 命中的**整头**，含已删除 scope 行；不是只检查命中的一行。它不计算收货量，也不冻结稍后的历史变化。
5. Material 覆盖 SQL 保留原 eligible/ranked 以及相关收货子查询在一个语句内，改 SELECT INTO 为 SELECT 并缓冲 LinkId、TargetLineId、decimal? Coverage 局部集合。最终采购 SQL 用带明确 uuid[]/numeric[] 类型的等长数组和 unnest 构造此关系（含空集合及 NULL）；SQL 对该冻结关系 JOIN、SUM、计数。保持每项配对、重复/缺失键行为，禁止第二次运行覆盖 SQL或为展示重新分摊库存。不要求新增 Core/Contracts 契约或全局助手。
6. 原 DTO 默认值、nullable 字段、错误消息、理由优先级和已有 C# 汇总继续保留。服务仍对请求取消的 OperationCanceledException 向上传播，对 UnverifiedReceiptHistory 维持“正式入库反向历史需核实，收货量未知。”；其他失败遵循当前分类。不得把普通取消、PG SQL 错误或 40001 伪装成历史未知。

独立命令的目的在于保留源阶段划分并避开驱动添加批次事务，不意味着已经证明跨库并发完全等价：源隔离配置、provider 时序及空头行为仍需验收。

## 批次、隔离与空头的证据

[Npgsql 官方 batching 文档](https://www.npgsql.org/doc/basic-usage.html#batching)说明没有显式事务时 NpgsqlBatch 会隐式包事务，失败后跳过后续语句并回滚批次；同页将分号拼接列为 legacy batching。因此不能把源 QueryMultiple 字符串直接机械搬成 PG 多语句，并忽略隐式事务及错误后执行边界。此处未针对项目 Npgsql 10.0.3 的每种 legacy 行为运行实测，建议独立命令明确边界。

[PostgreSQL 隔离文档](https://www.postgresql.org/docs/18/transaction-iso.html)说明 Read Committed 的各命令可使用不同快照，Repeatable Read/Serializable 的事务读具有不同快照约束。**隐式批次事务不等于 Read Committed 单快照**；整条 Trace 折成一个 SQL 则把不同阶段读压成一个语句快照，仍有可观察差异。仓储不应主动提升隔离。源 Trace 未自建事务、没有 HOLDLOCK；但 SQL Server 实例 RCSI、调用方隔离和实际 SqlClient 执行时序本轮没有数据库证据，不能凭“默认 Read Committed”断言所有源行为。

两类源均 `using var multi`，`await ReadSingleOrDefaultAsync<TraceHeader>()` 后空头立即 return null。读取 [Dapper 2.1.66 异步 GridReader 源码](https://raw.githubusercontent.com/DapperLib/Dapper/2.1.66/Dapper/SqlMapper.GridReader.Async.cs)，ReadRowAsyncImpl 即使读到零行也先调用 OnAfterGridAsync，内部推进 NextResultAsync 后才返回。读取 [同步 Dispose 源码](https://raw.githubusercontent.com/DapperLib/Dapper/2.1.66/Dapper/SqlMapper.GridReader.cs)，未关闭 reader 时会先 Command.Cancel，再释放 reader/command。源使用同步 using，不能用 DisposeAsync 代替其证据。

能够确定的是：空头 C# 返回并非“读取头后立即阻止一切后续执行”，也不是“自然消费完整批次”。不能确定的是：下游查询/覆盖计算/尾部 DROP 已执行多少、后续错误何时可见、取消是否遗留源临时表。这些需要源 provider 的真实样例。尤其 Guard 在头之前，即使头缺失，已关联的坏入库历史仍可能先报未知，不能将 PG 头查询提前到 Guard 前。

主控需冻结空头协议：在隔离样例中记录空头后的可观察后续错误、执行进度、取消和临时表残留，再决定 PG 是否短路。若选短路作为新行为，应显式登记差异；若选择继续查询到尾部，也不能仅凭推测宣称与源等价。本报告不盲选任一方案。

## SQL 转换约束

- WITH 的单语句作用域要保留；源 SELECT INTO 临时表的生命周期由局部集合替代，不引入跨命令 CTE 幻觉。Material eligible/ranked 与覆盖计算保留同一 SQL；不要把整个 Trace 合并成 MATERIALIZED CTE。
- OUTER APPLY 改 LEFT JOIN LATERAL ON TRUE，CROSS/内联语义分别保持；TOP 1 改 LIMIT 1，保留原 ORDER BY 和缺记录的 NULL。Progress Report 的 Sequence/UId 尾键不能丢。
- bit 改 boolean/CASE 时保留真、假与 nullable 语义，不把未知数值 COALESCE 成 0。聚合无行、存在 NULL、无匹配覆盖项的情况分别保持源表达式；PG COUNT 的 bigint 到原 int 接收边界须显式验证。
- 窗口保留 PARTITION、排序及 ROWS；需求日期/来源行号 NULL 最后是业务优先级。普通源 ASC 通常 NULL 在前，PG 默认不同，采购订单号/行号、装箱 PostedDate 等需按源表达式显式处理 NULL 顺序。
- 单位的既有 ASCII trim、空字符串、NULL 及原始比较保持区别。Gross 非法装箱计数中原始单位比较与展示中的 trim 比较不同，不顺手合并；指纹/来源版本的资格条件不借 Product 反推。
- 日期、decimal、Guid、枚举与现有 PG 参数/名称映射复用。Link Uid 并列顺序和文本 ORDER BY 涉及跨库比较协议，不能把 SQL Server uniqueidentifier 顺序或冻结 ICU 排序自动认定与 PG 相同；主控沿用已登记比较门槛。本轮不重新开展 B07/B10 探针或以新增样例绕过未闭合项。

## 已有测试证据与真实验收屏障

只读取，未运行。源 `SwiftExport.Application.Tests/DemandPlanning/MaterialDemandTraceSqlTests.cs` 已有两条 6 的承接共用采购量 12、收货 7 得到 6/1；还覆盖无关坏历史、相关反向历史未知、历史拆解、指纹/单位异常。`GrossDemandTraceSqlTests.cs` 覆盖历史 Link/源修订、缺采购目标、相关坏历史，以及 posted/reversed 装箱、快照错误使交付未知、**仅来源指纹/版本错误仍保留交付 4**。保留这些有效断言。

PG 历史 Guard 的 Integration 测试已有完整反向、已删除 scope 行仍检查整头、同头其他坏行、无关坏头、服务历史消息及预取消。服务消息测试使用仅调用 Guard 后返回 null 的替身仓储，**不是两类 PG Trace 的集成通过证据**。这些测试与主控已完成历史验收可以复用，但不能代替 Trace 的阶段并发验证。

主控后续在明确授权的隔离库范围内需要以下屏障；本轮没有数据库写入授权，不创建或运行夹具：

| 屏障 | 可验证目标 |
| --- | --- |
| 6+6/收货7，跨需求承接 | 两个 Trace 分别覆盖6/1；不是只按当前需求分摊；未知资格项使分区 NULL；负量/超量/单位错不被当零 |
| 初始 scope 与覆盖之间更新 Link | 证明 Guard 使用最初 scope、覆盖使用稍后新范围；不能用同一 Guid[] 误收紧；记录源既有缺口，不自动强化 |
| scope 命中头的另一坏行/已删除 scope 行 | 检查整头且阻断；无关坏头不阻断；没有 Link 时不是全库 Guard |
| 覆盖后、采购展示前另一连接提交收货 | 既有覆盖不变化，展示量可反映稍后的收货；按原 SQL 计算外围量。与在覆盖前提交的结果区分；靠可靠执行屏障，不靠延时猜测 |
| Gross Materials/Purchases/Deliveries/计数之间提交 | 不被一个大 SQL 强行压成单快照；装箱计数与展示发生变化时保持理由阻断规则 |
| 外层事务 null / Read Committed / Repeatable Read及必要Serializable | 同一连接、同一事务对象；读到调用者未提交写入；仓储不结束事务或改隔离；快照按调用者协议；源隔离配置同时登记 |
| 空头、有 scope 坏历史、空头后可见错误 | Guard 仍先执行；测清源 NextResult/Dispose 的观察结果后定 PG 短路策略；不推测后续 DROP |
| 各阶段等待中取消、SQL错误、业务 Guard 异常 | 请求取消按原服务传播；Reader/Command 释放，无剩余临时资源。null事务连接随后可执行查询；外层事务服务器错误可能进入 aborted 状态，须由调用者回滚后验证可复用，不能在仓储自行回滚或保证错误后原事务仍可查询 |
| 结束/重复调用与资源检查 | 正常/空头/取消/异常后无 PG 临时表或新函数；连接未被 Trace 关闭，不残留活动 reader。多轮并列、空集合、nullable数组投影验证覆盖关系配对 |

Guard 在 SQL 布尔查询完成后由 C# 抛业务历史异常，与 PG 服务器错误导致外层事务 aborted 要分开测试。原 SQL THROW 的事务影响也不能凭经验认定完全等价。取消后复用测试既要检查没有活动 reader，也要尊重调用者拥有的事务恢复边界。未在原 C# 汇总尾部检查令牌的行为，不在迁移中随意追加改变成功/取消优先级的最终检查。

## 待主控定案

1. 采纳独立命令、同一既有连接及可空外层事务的阶段协议，冻结 source scope 与 coverage 重新取范围、冻结覆盖复用规则。
2. 实测源空头 NextResult/同步 Dispose 后的可观察下游行为，冻结 PG 空头策略；此项在落地前保持未验证。
3. 记录源隔离设置及真实阶段屏障方式，明确并列 Guid/文本排序沿用哪项已冻结比较协议；不能仅用顺序调用模拟实际源 server 时序。
4. 由同一归属实现 Material 两个 partial，主控按上述屏障验收；本窗口本轮不接管实现、实库或修改清单。

结果写入后更新执行 B 为停写，再向主控返回绝对路径。此交付是读取协议审查结果，不是功能实现或兼容验收结论。

## 主控接回核对（2026-10-05 03:46）

上述覆盖排序描述含“拆解行号”不符当前源码。Material原ranked窗口71–75行实际只有DemandRequiredDate（NULL最后）、SourceLineNumber（NULL最后）、LinkId，没有NetDemand.LineNumber；原报告保留，实施以实际SQL为准，不添加排序键。主控源只读4探针和真实PG4常量协议场景通过，见[读取协议冻结](../Trace读取协议冻结.md)。源RCSI关闭、Snapshot OFF已只读核实。窗口LinkId尾键会改变覆盖归属，不能用普通PG UUID序替代已实测SqlGuid字节顺序；该调整不扩成全库UUID框架。
