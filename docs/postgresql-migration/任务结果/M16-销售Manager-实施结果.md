# M16 销售Manager实施结果

轮次：M16-S01-IMPL-01。执行A：01a10790-07d0-7a61-91e3-ed83ca6e34d4。
工作目录：`C:/Users/BigBigBigDv/.codex/worktrees/9b64/SwiftExport`；detached HEAD基线56209d97a00533c938dd527fa9d37683064a11e8。

## 写入范围与实现

本轮新增以下三份源码，并新增本结果、更新本人唯一状态；修改业务读取实现，未接完整ISalesOrderQueryRepository或DI。主Agent为Backend，遵循后端API工作流、Dapper/会话/质量门禁。未启动代理或聊天、未提交推送。

| 新增文件（绝对路径） | SHA256 |
| --- | --- |
| `C:/Users/BigBigBigDv/.codex/worktrees/9b64/SwiftExport/SwiftExport.Infrastructure/Repositories/SalesOrders/Query/PostgreSqlSalesOrderQueryRepository.Manager.cs` | `30D49290B08F72C100A40C53D6D97FB67075270EBC3B923FD88F2F4D555296DD` |
| `C:/Users/BigBigBigDv/.codex/worktrees/9b64/SwiftExport/SwiftExport.Application.Tests/Database/PostgreSqlSalesOrderManagerMigrationTests.cs` | `BC50CD88597ACD3763562F294BA847DA392C8DCF7644268B1F2979DFFFA9DA9A` |
| `C:/Users/BigBigBigDv/.codex/worktrees/9b64/SwiftExport/SwiftExport.Application.Tests/Database/PostgreSqlSalesOrderManagerMigrationTests.Integration.cs` | `D74812F785C5B018AFBCD76C640BF6BEE62A00240E30CFBC2E6CCDFFB1CF7B57` |

1. Manager公开入口按候选七字段、一次既有LineFacts、冻结关系内SQL聚合/QuickView/count、当前真实头和客户页四阶段执行。只传递缓冲JSON，不在C#重算事实。独立count在空页/超末页和最终真实头缺失时保留。最终页不追加头is_deleted条件。
2. 保留源分页1/20/200、普通筛选、字面SqlLikePattern、集合去空白和OrdinalIgnoreCase去重、日期按日及排他次日上界、QuickView未知值空匹配、SQL NULL求和、无行LEFT JOIN与单位门槛。总量不增加ReviewCount=0，超发保留12/0；混合单位本身不触发Review。
3. UnitCount/MAX和ReviewReason去重/排序采用既有字典比较。最终排序为业务交期ASC NULLS FIRST、CreatedDate DESC NULLS LAST和既有源SqlGuid字节序DESC。原因只在SQL拼接，DTO全部字段使用quoted CLR aliases及源商业默认值。
4. 所有阶段借用context.Session连接及可空事务，传递token，无新连接/事务/临时表/锁。有效交期参数始终显式timestamp，包括NULL。
5. 新文件内私有ManagerParameters只在Dapper命令克隆后显式指定新增int[]及冻结JSON类型；复用既有PostgreSqlParameters，不改公共实现或已验私有成员。候选/聚合/页经本包装执行；一次行事实仍直接调用既有FreezeLineFactsAsync及其JSON CAST。

## 验证与首轮反馈

清除SWIFTEXPORT_RUN_PG_TESTS、SWIFTEXPORT_RUN_PG_FAILURE_TESTS、SWIFTEXPORT_RUN_PG_COMMAND_TESTS、SWIFTEXPORT_RUN_PG_RBAC_TESTS及SWIFTEXPORT_RUN_SQL_SOURCE_READ_TESTS后，仅执行本轮离线过滤及必要受影响编译。

```powershell
dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore --filter "FullyQualifiedName~PostgreSqlSalesOrderManagerMigrationTests" --logger "trx;LogFileName=M16-S01-IMPL-01-final-offline.trx" --results-directory .artifacts/M16-S01-IMPL-01 -v quiet
```

- 最终19通过、0失败、0跳过。Application.Tests及依赖项目编译成功；7条既有MSTest分析器警告，未借机修改旧测试。
- 首轮离线16通过/3失败，记录未隐去：新增状态数组没有显式int[]类型、冻结JSON断言需与实际绑定类型一致；已在本文件私有参数包及本轮测试内收敛。未修改共享参数或只读销售partial。
- 离线从公开入口经真实Dapper记录四阶段、7字段候选、JSON/数组/时间类型、事务/token、分页与独立count、全部DTO aliases、数量门槛SQL及取消；内存返回不证明PG事实计算正确。
- 最终TRX：`C:/Users/BigBigBigDv/.codex/worktrees/9b64/SwiftExport/.artifacts/M16-S01-IMPL-01/M16-S01-IMPL-01-final-offline.trx`。
- 真实PG独立类PostgreSqlSalesOrderManagerMigrationIntegrationTests已编写并编译：53例覆盖全部快捷条件、NULL/零/超发、多行/单位字典比较、原因去重排序、SqlGuid分页及超末页、所有通用搜索字段、删除过滤、日期两端/单端、字面特殊字符/ASCII空格/问号/Tab、当前客户/NULL身份、同连接未提交写入及四阶段混合观察、候选后软删头或物理移除空页、预取消和阶段取消、无事务连续读取。
- 集成测试只复用严格故障库门禁，随机UID/商业键，本例事务finally回滚；无DDL/提交/清库。物理移除场景仅在测试事务内删除该例新建随机空头；测试不依赖或改写现有身份。
- 本窗口未连接PG/SQL、未执行任何集成项/数据库写入或DDL、未跑全量或旧Detail/LineFacts测试。真实PG事实、跨连接并发仍待主控串行验收，不将此前47实库通过或本轮离线通过提升为Manager实库验收。

## 只读依赖固定SHA复核

| 文件 | 本轮前后相同SHA256 |
| --- | --- |
| `SwiftExport.Infrastructure/Repositories/SalesOrders/Query/PostgreSqlSalesOrderQueryRepository.cs` | `F73D3B017D4326D7D127469C02BAED71E83E1CCF286A9931C5B7914AA7C21E99` |
| `SwiftExport.Infrastructure/Repositories/SalesOrders/Query/PostgreSqlSalesOrderQueryRepository.Detail.cs` | `21D60B231986AF89CB8EBA9DF65851F230A5B8F038BC159BE5CBE1137DA9F135` |
| `SwiftExport.Infrastructure/Repositories/SalesOrders/Query/PostgreSqlSalesOrderQueryRepository.LineFacts.cs` | `B379FBB461262888984813F4C04193FB8B222000302155708C1D730C33FD0AEB` |

三份只读销售partial一致；所有旧测试、源SQL、DTO/Core、服务、DI及共享Guard均未由本轮修改。已检查新文件完整实现及手工diff/空白，新增源码无尾空格。git diff --check无错误；工作区既有PostgreSqlExceptionClassifier改动产生LF/CRLF提示，属既有同步，不由本轮改写。

## 限制与接回

B10全Unicode字典排序/匹配与SQL Server的精确等价仍未闭合，本轮仅沿已冻结字典比较实现，不修改ICU、增加通用适配框架或用.NET Ordinal代替SQL。未宣称完整查询接口/Fulfillment或运行接线完成。

主控按表中三文件固定SHA接回，审查diff后单独筛选PostgreSqlSalesOrderManagerMigrationIntegrationTests串行PG验收；不要把离线类过滤字符串用于集成类。所有旧结果保留。连接字符串未输出/写入成果；新增文件当前未提交。
