# M14 两Trace主控切片验收

2026-10-05。执行A/B分别本人STOPPED且wait_threads确认idle/completed后，按各自五份交付的固定源/目标SHA精确接回。A主文件改动仅完整物料查询接口及职责说明，旧工作台公开方法到全部私有实现逐字核对未变；B只声明来源Trace切片，没有补其他三个接口方法占位。没有整树覆盖、业务服务/DTO/API/DI改动或Git提交/推送/部署。

## 交付与当前证据

原始交付/固定SHA/本人命令保留于M14-物料Trace-实施结果.md和M14-来源Trace-实施结果.md。共享协议以[主控冻结](../Trace读取协议冻结.md)为准，不将B早期误写Net.LineNumber的审查描述作为来源覆盖排序依据。

| 范围 | 当前主控验证 | 准确含义 |
|---|---|---|
| 物料Trace | 20离线/46真实PG通过 | 冻结scope、原Guard、header→tree空头、一次coverage→冻结数组采购、原C#理由及汇总；合法事实/单事务阶段变化/取消恢复 |
| 来源Trace | 36离线/39真实PG通过 | header→materials缓冲→采购/箱单/异常计数→原C#理由与交付；历史物料、整采购行、来源理由独立 |
| 共享Guard/分类 | 最新两个Trace+分类合计59离线、Guard24真实PG复验通过 | uuid[]显式绑定修正后的实际公开入口；59=20+36+3，不重复累计历史47相关离线 |

两个实库Trace分别46/39，不把Guard24、源SQL4/PG协议4、旧工作台50和来源四方法34合并为Trace当前数量。共享报告Sequence并列受唯一索引禁止，Material场景实测23505/约束名；生产尾键仍保留源SqlGuid字节顺序，不能造数绕索引证明并列报告选择。98样本源/PG只读UUID比较已证明局部排序表达式，不等于全部查询排序验收。

## 实际命令

仅离线时关闭四PG开关和SQL_SOURCE_READ，实际合入复验：

```powershell
dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore -p:UseSharedCompilation=false --filter 'FullyQualifiedName~PostgreSqlMaterialDemandTraceMigrationTests|FullyQualifiedName~PostgreSqlGrossDemandTraceMigrationTests.Offline_|FullyQualifiedName~PostgreSqlInboundHistoryMigrationTests' --results-directory TestResults/postgresql-migration --logger 'trx;LogFileName=m14-traces-guard-main-offline.trx' -v minimal
```

真实只启RUN_PG_FAILURE_TESTS，固定192.168.3.12:5432 / dv_next_pg_failure_tests_20261004，主控串行且随机单事务finally回滚；无DDL、清库、备份写入、序列重置：

```powershell
dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore -p:UseSharedCompilation=false --filter 'FullyQualifiedName~PostgreSqlMaterialDemandTraceMigrationIntegrationTests' --results-directory TestResults/postgresql-migration --logger 'trx;LogFileName=m14-material-trace-main-array-fixed-real-pg.trx' -v minimal
dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore -p:UseSharedCompilation=false --filter 'FullyQualifiedName~PostgreSqlGrossDemandTraceMigrationTests.RealPg_|FullyQualifiedName~PostgreSqlInboundHistoryMigrationIntegrationTests' --results-directory TestResults/postgresql-migration --logger 'trx;LogFileName=m14-gross-trace-guard-main-first-real-pg.trx' -v minimal
dotnet build SwiftExport-WebAPI.slnx --no-restore -p:UseSharedCompilation=false -v minimal
```

Material46通过，Gross+Guard合计63通过（39+24），0失败/0跳过。必要项目完整编译0错误/10既有警告；最后后端阶段0错误/3既有NU1900。只局部测试，没有全量测试。

## 首轮回归与主控公共修正

Material首轮46项为6通过40失败，错误集中42809（ANY右侧不是数组）及42883（uuid=text）；TRX m14-material-trace-main-first-real-pg.trx原样留存。原因是共享PostgreSqlInboundReceiptHistoryGuard匿名Guid[]绑定依赖Dapper从连接类型判断数组能力，真实驱动包装器使参数展开；源/PG业务判断条件没有问题。

主控只在该共享文件ValidateAsync使用既有PostgreSqlParameters.AddIds和Scoped标量，显式uuid[]保留空数组/空范围，不靠包装器命名伪装提供者，不建设通用框架。当前共享文件SHA为7F5964503D8FB7BA35DFE2BA2C7941C3D7EEE16F3ADA94D93D589D03AB96925F，前版C6578ADD901BBDE13B8178A06A5AB00CE0126C2D938AE534FFF8BE0C703E291C。46原断言、59相关离线及24原历史实库均复验通过。两worktree本人停写时固定旧/新SHA同步，后续只读。

本回归已修复，不删除原失败或修改业务条件/断言。B离线测试曾通过模拟Npgsql能力标识避免匿名数组展开，其历史记录不修改；主控真实包装器证据和显式绑定实现是当前事实。

## 准确验证边界

真实Material覆盖6+6/7→6/1，日期/来源行号优先及源UUID尾键，不含Net.LineNumber；同事务阶段变动后收货展示8仍沿用旧6/1覆盖，外围1。起始scope与后续Link范围独立，无额外Guard。来源Ordinal MD/指纹判定、未知量、超量Associated保留、历史/合法坏单位/客户/版本、相关整头门禁及空头缓冲已执行。

Gross源理由与交付理由独立，坏指纹仍Delivered4；反向箱单保留正历史而不计净量、软删物料/历史Link、合法坏历史与相关范围、整采购行反馈及原字段回落已执行。

阶段变化包装器当前同调用者事务自写，不是跨连接提交业务竞争。服务器取消用测试独立只读等待命令验证驱动取消/25P02及调用者回滚，不能声称已经证明各生产SQL在真实关系锁等待中的全部行为。Trace跨连接RC/RepeatableRead、真实阶段锁等待及性能仍由主控后续补验，M14-P03保持开发中。

50表行数/全字段摘要与销售Detail前基线完全一致，见M14-M16-主控实库摘要.csv。无结构变更待同步dv_next，備份只读，原数据保留。宿主仍SQL Server，未接PG运行。

下一轮A销售Manager、B来源分组/详情已经按后续查询实施分派.md放行；原始LIKE工作台、B07/B10和跨连接协议由主控持有。Material三个方法均已实现但接口整体跨连接验收仍待，Gross Trace只是四方法中的一项，不把整个需求模块标完成。
