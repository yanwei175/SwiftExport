# M16-S01 Manager主控验收

2026-10-05，A本人STOPPED/聊天idle completed后，按[本人结果](M16-销售Manager-实施结果.md)三个代码固定SHA及结果969ED82FF5BB55C3B029747B1A728406588FEAF63B795BED25513B57CF9FC576接回。普通沙箱第一次只复制Manager生产文件，第二份权限拒绝；授权定界升级后重验全部源/目标SHA，仅补缺失三份，没有覆盖异值或其他成果。

**19离线通过，54真实PG通过（53查询/协议场景、1非法零Qty约束拒绝）。** 主控对照源候选/事实/汇总/count/当前页及商业投影，生产Manager SHA仍30D49290B08F72C100A40C53D6D97FB67075270EBC3B923FD88F2F4D555296DD，与A交付相同。主控只修新增Integration夹具，当前SHA为7F42F24D3A1294B370D098E17F1FC6B3232BCAFBAA3FFC9F82E57E1CE40447CE；不覆盖A旧结果中的原始SHA。

## 首轮失败及真实约束依据

首轮53项52通过/1失败，在UnitComparisonAndZero(true)插入Qty=0时23514 ck_sales_order_line_qty；尚未执行查询。主控只读核实源sys.check_constraints CK_SalesOrderLine_Qty及PG pg_get_constraintdef，两者均Qty>0。不是迁移回归或环境失败，也不允许放宽约束伪造合法零Qty。

原false合法单位/未定交期断言全部保留；true改为断言明确23514及约束名，finally回滚失败事务，不宣称零订单量查询通过。另增合法正订单Qty10且Posted交付10的用例，验证TotalQty10/Shipped10/Unshipped0/无未定开放行/无Review。生产逻辑与结构无改动。首轮TRX留存，修正后54全部通过，未删除有效业务断言或隐藏失败。

## 实际验证

仅192.168.3.12:5432/dv_next_pg_failure_tests_20261004，随机UID和商业键单事务finally回滚，无初始化/清库/序列重置/DDL。关闭其他PG及source开关，仅真PG时设置故障测试开关和明确故障库连接，凭据静默读取。

```powershell
dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore -p:UseSharedCompilation=false --filter 'FullyQualifiedName~PostgreSqlSalesOrderManagerMigrationTests' --results-directory TestResults/postgresql-migration --logger 'trx;LogFileName=m16-manager-main-offline.trx' -v minimal
dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore -p:UseSharedCompilation=false --filter 'FullyQualifiedName~PostgreSqlSalesOrderManagerMigrationIntegrationTests' --results-directory TestResults/postgresql-migration --logger 'trx;LogFileName=m16-manager-main-first-real-pg.trx' -v minimal
dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore -p:UseSharedCompilation=false --filter 'FullyQualifiedName~PostgreSqlSalesOrderManagerMigrationIntegrationTests' --results-directory TestResults/postgresql-migration --logger 'trx;LogFileName=m16-manager-main-legal-fixture-real-pg.trx' -v minimal
```

必要编译0错误/10既有NU1900/MSTEST警告；无全量/提交/推送/部署。首轮及修正后全部50表行数/MD5不变，见[本批摘要](M14分组-M16Manager-主控实库摘要.csv)。备份只读，本批无结构变更故不涉及dv_next同步。

覆盖QuickView全部及未知、数量与核实门槛、多单位/超发/NULL/原因、字面搜索/集合/日期、超末页独立count/源UUID排序、可空事务/自写/分阶段变化/取消；这些阶段变化来自同事务写入，不能冒称Manager跨连接竞争通过。Fulfillment由B开发，三个完整方法/接口/SaveClose服务链/DI及B10仍待；Detail47和本54不累计为M16整体运行验收。A已接续采购只读审查，Manager及主控夹具归主控。
