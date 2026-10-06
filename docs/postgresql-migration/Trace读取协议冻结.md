# M14-P03 Trace读取协议冻结

2026-10-05 03:46，主控基于当前源码、B的[停写审查](任务结果/M14-Trace读取协议-审查结果.md)、4源只读及4真实PG协议探针冻结以下实施门槛。此页允许下一批独立仓储开发，不代表完整Trace已经验证或运行接线。

## 阶段与所有权

两类Trace借用既有DbSession同一连接及可空外层事务，每阶段独立参数化命令；不建仓储事务/连接/数据库函数/临时表，不改变隔离，不加业务锁。上一阶段完整结束再进入下一阶段，所有SQL保留原token。PG显式/legacy批处理可能引入事务，不能机械移植源整批QueryMultiple；[Npgsql batching](https://www.npgsql.org/doc/basic-usage.html#batching)。外层RepeatableRead/Serializable已有快照由调用者决定，不把它刷新为最新数据。

Gross：一次scope→共享ScopedGuard→header→materials→purchases→deliveries→非法箱单历史计数→原C#汇总。Material：一次scope→共享ScopedGuard→header→tree→一次SQL覆盖物化→采购SQL绑定冻结覆盖→原C#汇总。scope即起始时刻所有PO Link采购行，不过滤删除/历史Link；Guard校验命中整个头。覆盖候选按源稍后语句重新读取当前Link范围，不复用scope替代。无关坏历史不能阻断，空scope不是全库；不补第二次Guard或强化源既有检查时序。

源头ReadSingleOrDefaultAsync会先推进下一个结果，真实只读探针证明下一结果元数据之前错误可见，已有第二结果元数据后的尾部错误在空头同步Dispose路径可不被观察。不能宣称尾部SQL/DROP一定执行或一定未执行，不能建立整Trace单快照。

PG采用可评审的最小稳定读取边界：先header，再完整缓冲第二个只读集合（Material tree/Gross materials），**随后**若header为空返回null，不开始其余阶段。这样保留下一结果阶段可见SQL失败并避免内部取消活动reader破坏调用者事务；不提前到Guard之前短路，也不无条件执行整个尾部。完整缓冲相对源仅推进元数据增加读取工作，是避免跨驱动未读reader/隐式批事务的必要基础设施调整，不改变合法数据的null/业务DTO、锁/事务所有权或原C#数量算法。源provider预读深度、畸形数据下行映射错误及尾部技术错误可见范围不作跨驱动完全同一的保证；需新增真实空头/软删头、外层事务仍可继续查询、取消/错误释放案例，出现实质业务返回差异向主控报告。

A独占Material主文件/新.Trace.cs及本轮新测试，实现第三方法后才完整声明现有IMaterialDemandQueryRepository。B独占Gross两个新partial和本轮新测试，当前只做Trace，主文件仅构造上下文，不声明完整接口/占位/DI；其余三方法未来仍同归属。主控独占共享Guard/classifier、主清单、协议和数据库；公共文件只精确停写同步，不允许各窗口改共享实现。

## Material冻结覆盖关系与尾键

eligible/ranked/正式收货/单位异常子查询在**一次SQL**内计算并缓冲LinkId、TargetLineId、decimal?覆盖量，作为源#MaterialReceiptCoverage的局部等价副本。最终采购SQL以等长uuid[]/uuid[]/numeric[]及多数组unnest重建冻结关系，缺项/NULL/空集分别保留。金额计算、窗口、SUM、NULL计数和外围量仍由SQL执行，不用C#重新分摊，不重新查询覆盖阶段。展示阶段收货量重新读取，覆盖仍旧值；例如覆盖阶段7→6/1，展示阶段8仍6/1，并依原SQL算外围量。

窗口原顺序为日期NULL最后、SourceLineNumber NULL最后、LinkId；**没有拆解行号**，B审查的该处描述已核对纠正。UUID并列尾键会改变覆盖归属，必须保留源SQLServer排序而不是普通PG UUID序。主控以SqlGuid期望和源ORDER BY实际核验98个固定逐字节/随机UUID，PG以下规范字节优先级实库通过（uuid_send为规范字节，Guid.ToByteArray前3字段端序不同）：

`10,11,12,13,14,15,8,9,7,6,5,4,3,2,1,0`

在该窗口使用整数ARRAY[get_byte(uuid_send(LinkId),上述各偏移)]作为原LinkId排序表达式，保留ROWS UNBOUNDED PRECEDING。不新增排序键/优先级，不引入持久排序列、函数或全库通用框架。其他展示顺序沿已登记边界，但会影响latest反馈报告选择的Uid DESC也应保留源优先级，不允许以展示排序差异掩盖最新记录变化。后续仓储须真实测6+6/7、日期/行号并列、source日期优先和最新报告Sequence并列。

## 语义冻结

Material tree按同Gross全部未删修订，不改Parent遍历；覆盖候选包含相同采购行的其他需求，各无效候选使整分区NULL，不提前筛掉。缺目标内部INNER排除与展示LEFT保留不统一。IsActiveLink、共享判断、历史/图像/单位/来源版本与Gross差异保持。超量时原associated数值保留，coverage未知；不据理由统一将量全部NULL或0。GetSourceReviewReason及原C#汇总保留Ordinal比较/IsNullOrWhiteSpace，不能把SQL ICU语义扩进C#。

Gross历史/删除物料展示、source版本理由与delivery理由分离，只有delivery理由阻断DeliveredQty；旧明确例子来源指纹坏仍交付4须保留。反向箱单展示正Qty但不计正式净量，不再减一次；被软删已过账或孤儿箱单由尾部计数独立挡住净量。原原始单位比较与修剪比较不合并。单位仅ASCII trim，数量未知与零分开；COUNT到int、nullable枚举/decimal/日期/UUID及真实物理列quantity/显式DTO别名逐项核验。B07/B10保留，不展开字符组修复。

PG Guard以C#专用异常匹配现有分类，当前已验证源业务错误。源默认XACT_ABORT关闭时THROW51001的只读外层事务XACT_STATE仍1，真实探针通过；PG typedGuard抛出后调用者事务应仍可用。不把服务器取消/SQL错误与业务Guard混同：前两者在PG外层事务可能aborted，由外层决定回滚，仓储不能偷偷恢复/回滚/提交。

## 已执行命令与证据

源SQLServer 192.168.3.12/dv_next只读核验版本15.0.2000.5、RCSI=false、Snapshot OFF、compatibility150，未调整设置。源会话factory默认隔离由驱动/调用者决定，命令只常量SELECT、WAITFOR、专用THROW及只读Serializable事务finally回滚；无临时表/业务数据/DDL。连接只从已授权配置派生，不打印凭据。

```powershell
dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore -p:UseSharedCompilation=false --filter 'FullyQualifiedName~SqlServerSourceReadProtocolIntegrationTests' --results-directory TestResults/postgresql-migration --logger 'trx;LogFileName=m14-source-read-protocol-probes.trx' -v minimal
dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore -p:UseSharedCompilation=false --filter 'FullyQualifiedName~PostgreSqlTraceReadProtocolIntegrationTests' --results-directory TestResults/postgresql-migration --logger 'trx;LogFileName=m14-trace-array-uuid-protocol-real-pg.trx' -v minimal
```

源4通过（空头两个错误位置、原THROW外层事务、UUID排序），PG4通过（UUID字节顺序、empty/null/paired冻结覆盖数组），编译0错误/10既有警告。源仅SOURCE_READ开关，PG仅FAILURE开关且固定dv_next_pg_failure_tests_20261004，其他开关清空。**这是协议探针，不是完整Trace通过数**。无生产部署/全量测试/提交推送/结构同步。

## 仓储阶段验收门槛

独立编译/离线只验证调用/参数/DTO/事务借用；主控串行真实PG证明完整查询SQL、6/1及并列尾键、历史/未知/缺目标、整头Guard、空头/软删头、预取消/阶段等待取消、同事务自写可见/返回后可继续和aborted由调用者回滚。阶段间更新以真实连接包装器控制**命令完成/下一命令开始**屏障，不新增生产测试回调、延时猜测或反射替代实际SQL。无外层事务跨连接变化、ReadCommitted和必要RepeatableRead分别证明旧覆盖复用及新展示量；源锁式ReadCommitted与PG MVCC逐行扫描不能据几例宣称全部并发完全等价。

已禁止构造的负量/非法Link要保留防线并登记未造数，不禁用CHECK。数据库只随机夹具事务回滚；确需可提交两连接变动只用新独立目标库/精确随机UID清理、保留日志，先由主控登记范围。本批执行窗口不实库，主控负责所有实例、初始化/清理范围及保护证据。
