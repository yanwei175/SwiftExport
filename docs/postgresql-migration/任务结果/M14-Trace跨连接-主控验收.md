# M14-P03 Trace跨连接本批验收

2026-10-05，主控新增独占 `PostgreSqlDemandTraceCrossConnectionTests.Integration.cs`，只把已有 Material Trace Integration 类型改为partial并同步夹具职责注释，复用其真实连接/随机实体帮助器，生产查询不改。本批 **13项真实PG通过，0失败/0跳过**，不是重跑原Material46/Gross39，也不等于完整P03或PG宿主完成。

## 实际命令和证据

固定实例192.168.3.12:5432，库 `dv_next_pg_failure_tests_20261004`，仅主控使用。凭据从已授权环境变量静默读取，用DbConnectionStringBuilder明确切换测试库；只开SWIFTEXPORT_RUN_PG_FAILURE_TESTS=1，其他PG/source开关关闭，无密码输出。

```powershell
dotnet build SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore -p:UseSharedCompilation=false -v minimal
dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore -p:UseSharedCompilation=false --filter 'FullyQualifiedName~PostgreSqlMaterialDemandTraceMigrationIntegrationTests.CrossConnection_' --results-directory TestResults/postgresql-migration --logger 'trx;LogFileName=m14-traces-cross-connection-first-real-pg.trx' -v minimal
```

必要编译0错误/10既有警告（3NU1900、7MSTEST）；后续增量执行13通过，3既有NU1900。TRX实际计数13/13，保留在上述路径。没有全量测试、DDL、备份写入、DI切换、Git提交/推送或部署。

## 真实独立连接及事务范围

- 3项覆盖冻结场景：无外层事务/ReadCommitted/RepeatableRead。生产coverage已缓冲后，另一个物理连接提交本例收货量7→8，显式检查两个backend pid不同。无事务/RC保留旧coverage1同时展示新收货8/范围外1，下次读取coverage2；RR沿调用方快照仍7/1，不另开事务。原事务引用不变。
- 6项首次门禁范围场景：Material/Gross分别覆盖上述三隔离方式。另一个物理连接在首次scope门禁后插入本例新Link，目标是原不在范围内且有合法坏历史的第二采购头。无事务/RC本次后续查询看到新Link，且首次门禁没有重跑；下次新scope触发UnverifiedReceiptHistoryException，业务门禁不使外层事务失效。RR仍原范围。不是同事务自写或延时猜测。
- 4项生产关系锁等待取消：Material/Gross各无外层事务/外层RC，独立持锁事务锁住demand_execution_link，第三物理连接用pg_blocking_pids及pg_stat_activity确认实际生产首条scope查询正在等待后取消。取消结束才释放持锁者；外层PG事务的25P02由调用方回滚恢复，仓储不接管。连接随后SELECT1、关系/元组/advisory锁及临时表0。不是测试pg_sleep代替生产等待。

## 数据保护与剩余事项

9项有提交的跨连接场景只提交本例随机UID夹具/新Link/数量变化；注册所有已插入身份（含supplier）。结束先回滚/释放读事务，再用独立事务按登记UID逆插入顺序精准DELETE，提交后逐集合确认0。部分种子失败仍由原事务回滚。4项等待场景不写数据。不清库、不删表、不改既有UID、不重置序列。

本批前后全部50张public表行数和MD5相同，见[M14-Trace跨连接实库摘要](M14-Trace跨连接实库摘要.csv)。测试清理已完成，不需要另行重置。未修改结构，故本批没有dv_next结构同步事项；备份只读。

P03继续开发中：本批证明指定scope/coverage阶段及首条生产scope的锁取消；其他生产读取阶段等待、完整SQL Server与PG跨连接竞争等价、性能和所有调用方/运行接线未验。既有Source4/PG4探针、Material46/Gross39单连接与本批13按范围分别记，不累计为完整模块通过。B07/B10仍开放，业务/API语义未变。

本批源码指纹：新增CrossConnection Integration SHA A0AD61139CC2F2730CA480B3799284FEFBA4FF1CDB095CB534BFB4243C3D0638；原Material Integration仅partial/职责注释衔接后的SHA B86A2E6625FA4A6A6D71FC0B6BEC81BCAE44312040A76C59BC8F625E41E8D55C。原A交付SHA保留于本人旧结果，不从旧worktree覆盖本批主控衔接。
