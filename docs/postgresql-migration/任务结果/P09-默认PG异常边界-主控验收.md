# P09 默认PG异常边界主控验收

2026-10-06 00:05，主控串行。本批完成退出旧实现前的最小默认值收敛，未删除旧仓储、SQL分类器或SqlClient包，不建设长期提供者切换。主Agent Backend / 后端API工作流，公共接口及会话由主控持有；A/B最新聊天均notLoaded且最后轮次completed，本批未续派或创建窗口。

## 实现与原语义边界

- Infrastructure/Data/DbConnectionFactory.cs：IDbConnectionFactory.Errors默认改用PostgreSqlExceptionClassifier；暂留SqlConnectionFactory显式提供SqlServerExceptionClassifier，保证显式源对照不借用PG分类。接口签名 / 连接创建 / 生命周期未改，未拆出新文件。
- Infrastructure/Data/DbSession.cs：Errors优先读取原factory.Errors，备用值改为PG。正常生产PostgreSqlConnectionFactory此前已显式提供PG分类器，故正常运行路径没有新的分类行为或业务分支；源工厂仍显式提供原SQL分类。连接打开 / 事务 / 清理 / 锁实现未改。
- DatabaseBoundaryTests既有源错误用例从显式SqlConnectionFactory获得分类器；共享会话用例不再由测试替身覆盖真实接口默认值。
- BusinessCommandExecutorTests既有六种序列化失败 / 清理分支去掉私有_errors反射注入，直接检验会话默认PG分类。不新增用例或矩阵、不改有效断言。真实40001仍为ServiceUnavailable、单请求不自动重试；首次提交结果未知、清理与HTTP协议不由本批重写。

源码事实优先：A于19:11的审查仍作为当时快照保留；其中两处公共默认SQL引用已在本批收敛，但SQL工厂自身的显式源引用及旧类型 / 项目包依赖仍存在，不能宣布SQL Server已经退出。P08剩余门禁及B07/B10/B19比较决策仍是完整删除前置。

## 实际验证

一次必要编译：dotnet build SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore -v minimal，exit0，0错误 / 7既有MSTEST警告，10.94秒；该编译同时生成受依赖的Infrastructure / Application / WebApi及原工具项目。使用已授权依赖和工具obj，没有恢复包或改项目引用。

只运行以下现有受影响边界，不运行全量，也不重跑前批真实PG绿色：

```powershell
dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~DatabaseBoundaryTests|FullyQualifiedName~BusinessCommandExecutorTests.SerializationFailure_IsKnownAbortWithoutAutomaticRetry|FullyQualifiedName~PostgreSqlFoundationTests.Factories_CreateClosedConnections|FullyQualifiedName~PostgreSqlFoundationTests.ProviderErrors_StayDistinct|FullyQualifiedName~PostgreSqlFoundationTests.TransportErrors_DoNotGuessFromMessages' --results-directory TestResults/postgresql-migration --logger 'trx;LogFileName=p09-default-pg-error-boundary.trx' -v minimal
```

原生结果 **39通过 / 0失败 / 0跳过，114ms**，exit0。[原生TRX](../../../TestResults/postgresql-migration/p09-default-pg-error-boundary.trx)。覆盖源Number分类及原HTTP映射、接口默认共享会话、PG SQLSTATE / 网络取消分类、无构造默认会话六种40001执行 / 提交 / 重放清理分支。连接替身和工厂closed连接均不打开数据库，不能替代真实锁 / 事务 / 并发或最终P08验收。

本批没有数据库连接、读写、DDL、结构同步、初始化或清理，也没有改变用户配置、账号、服务规则、DTO / API / 仓储结构 / 隔离或锁。上批failure52454行的终态证据保留，本批未重新查询或改写；不凭旧数字宣称当前外部库再次核验。所有未提交成果保留，无提交 / 合并 / 推送 / 部署。

当前源码SHA256：

| 文件 | SHA256 |
|---|---|
| SwiftExport.Infrastructure/Data/DbConnectionFactory.cs | 6C231375AD594E76213587467BD73B692F475A1D1273DD3BC7460AE23DF5D948 |
| SwiftExport.Infrastructure/Data/DbSession.cs | B71CB075F9C5CE9288BBC2B1EFC58FA9362283C5FC2ED3773D54FFF949194AAE |
| SwiftExport.Application.Tests/Database/DatabaseBoundaryTests.cs | 755F672BDB9361ED69F1D189FD3BDAA7059842AD3C9B95710191CA7D71EE5613 |
| SwiftExport.Application.Tests/Commands/BusinessCommandExecutorTests.cs | 535F4D593EEE316795027D2D1FF6C180571D26029DB391821E5EEE34ED7D0421 |

证据整理首次PowerShell foreach直接接管道语法失败，修正只读提取后获得上述哈希和39个TRX计数；未重跑编译 / 测试，不归为生产或测试失败。git diff --check通过。主控本批STOPPED，整体目标active。

