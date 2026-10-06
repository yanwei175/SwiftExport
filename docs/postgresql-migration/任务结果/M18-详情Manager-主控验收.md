# M18 进度详情与采购Manager主控验收

2026-10-05，现有A/B本人STOPPED且聊天completed后，逐文件固定SHA接回；旧报告保持原内容。新增基础设施查询和测试，未修改Application/Core/Contracts/API业务规则或DI。受测切片验证通过，完整跨连接协议及PG运行接线未完成。

| 切片 | 本轮主控离线 | 真实PG | 结果边界 |
|---|---:|---:|---|
| Progress GetDetail | 22 | 30通过 | 一次Guard后三结果、缺头推进及Dispose、当前图片/NULL、正式收货/完整Reversed、历史排序、连接与父事务、取消 |
| Manager Search/GetLines | 25 | 最终40通过 | 全局Guard和孤儿行校验、整行投影后冻结分页、商业量及状态/日期筛选、NULL和当前图片 |

合计47离线通过。首次合计70实库69通过1失败，失败为Manager新Product夹具漏填SupplyType，PG23502；源sys.columns只读确认SupplyType NOT NULL，PG同样必填。仅新增测试Product补SupplyType="采购"，保留有效断言/约束/生产SHA，Manager40复验全部通过；未重复跑已通过且未改变的Detail30。首轮失败TRX保留。不是生产回归，登记B20。

实际执行均 `dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore -p:UseSharedCompilation=false`，指定results-directory为TestResults/postgresql-migration，-v minimal：
- 清四PG及SQL_SOURCE_READ_TESTS开关，filter `(FullyQualifiedName~PostgreSqlPurchaseOrderProgressDetailMigrationTests|FullyQualifiedName~PostgreSqlPurchaseOrderManagerMigrationTests.Offline_)`，logger `trx;LogFileName=m18-detail-manager-main-offline.trx`。
- 故障库门禁，filter `(FullyQualifiedName~PostgreSqlPurchaseOrderProgressDetailMigrationIntegrationTests|FullyQualifiedName~PostgreSqlPurchaseOrderManagerMigrationTests.RealPg_)`，logger `trx;LogFileName=m18-detail-manager-main-first-real-pg.trx`。
- 故障库门禁，filter `FullyQualifiedName~PostgreSqlPurchaseOrderManagerMigrationTests.RealPg_`，logger `trx;LogFileName=m18-manager-main-legal-product-fixture-real-pg.trx`。

故障库192.168.3.12:5432/dv_next_pg_failure_tests_20261004，主控串行，合法随机单RC finally回滚。三次前后全部50表17595行/MD5一致，见[M18摘要](M18-详情Manager实库摘要.csv)。无初始化/DDL/清库/提交/序列消耗/备份修改，故无保留结构需同步dv_next。必要编译0错误，3既有NU1900及7既有MSTEST警告；未全量、Git提交/推送/部署。

固定SHA：
- PG Detail F7D0A338A5606D215D999B1AB34C8879CFD1B11E69D1C341376F62BF518E9711；离线D82175543A0901942BB7B05E1F8F61D8DA7C4C124387611F1C7A799FF81F549F；实库F68E80540FC8973DBA6B533650A482C46B59B07DBF018BB2F44D1A7F0B9401F0。
- PG Manager 6D0D43E98F2BD0DAC7DD2B00CC4A3BD450FEBD7E73079A8CDEB87077A0C2D674；离线5BAA3D8BC98092C06F2460D72BB35BEF32BCA20E126E55A84CDB65938CF43DD3。
- Manager原实库415B93D1B2251978D07E6F25E01ADD1FA9A717134B35D6C5AC70751FF32C8FDE；合法夹具后37B8E62CE7C289AB9C1D21482FE6345D7C422529016F657CFB3711FECAFA353C。
- A结果6FF87342B9C3852336CCE7C2838926E7BE703FFBB7C47BDF170B984D9C21DA94；B结果BD84902F1BB077DDC3C74FDF77212931C5290989289F8ADF7CE6D18EE50A4472。

仍未验：服务器语句中途真正独立连接变动/等待取消、大列表性能、B10/B19完整Unicode比较及完整服务/接线。整批开始前同事务自写不是物理跨连接证据，模拟单测不是PG验收。

