# M26-S 主控验收

2026-10-05，主仓库 HEAD40d16632 及未提交新增仓储/两测试。B 首轮、ASCII 修正轮及补矩阵文档轮均由本人状态和聊天确认停写；四交付目标均不存在，守卫后只添加四文件，没有覆盖其他成果。

三个真实读取方法保留外层事务可空、原数组和源短路、停用/断链/歧义、LinkSku 回退及商业编号 OR。主控预审在 SQL Server 只读常量证明数组参数尾 ASCII 空格应等值；B 在 SQL unnest 右侧 rtrim 补齐，不改 C# 输入，原错误零结果预期按源真值纠正。Unicode 比较完整缺口另见 B10 和 B 原结果，不改公共 ICU。

## 实际验证

- 先清除四个 PG 运行开关：`dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore -p:UseSharedCompilation=false --filter 'FullyQualifiedName~ExcelSales_|FullyQualifiedName~ForExcelSalesOrderTests|FullyQualifiedName~ForExcelSalesOrderContractTests|FullyQualifiedName~PackingListImportAndAssociationTests' --logger 'trx;LogFileName=m26-sales-merged-offline.trx' --results-directory TestResults/postgresql-migration -v minimal`：27通过/0失败/跳过，相关项目编译0错误，既有警告保留。
- 进程内既有配置只派生Database=dv_next_pg_failure_tests_20261004，打开SWIFTEXPORT_RUN_PG_FAILURE_TESTS=1并关闭其他开关：`dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-build --no-restore --filter 'TestCategory=PostgreSqlForExcelSalesIntegration' --logger 'trx;LogFileName=m26-sales-first-real-pg.trx' --results-directory TestResults/postgresql-migration -v minimal`：13通过/0失败/跳过，427ms。

固定192.168.3.12:5432/public入口；主控串行，随机实体单ReadCommitted事务全回滚，无事务场景只读随机未命中值，不初始化/提交/清库。主控再次核实50表/17,595行计数及全字段摘要与本阶段基线一致，advisory_locks=0、other_active_sessions=0。备份/dv_next无写入/DDL，未提交推送部署。

已验证是上述受测读取切片，不是完整M26：U+202F/U+205F/U+3000尾填充及NBSP类冻结ICU比较差异保持开放，采购HOLDLOCK及完整正式单据服务尚未迁移；执行中网络取消/跨连接一致性未覆盖。业务服务未改、DI未切换。B当前转P04-E独立生命周期测试，不沿用本轮源码写入授权。
