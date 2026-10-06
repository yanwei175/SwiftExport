# M18采购Trace及M20库存查询主控验收

2026-10-05主控。两窗口本人STOPPED及聊天completed一致，按固定SHA精确接回各4文件，原结果保留。仅新增PG基础设施仓储/测试/结果，未改原SQL仓储、Application/Core/Contracts/API业务规则或DI。两仓储完整实现各原接口，但未接PG运行。

| 切片 | 主控离线 | 真实PG | 验证结论 |
|---|---:|---:|---|
| 采购Trace Get | 40 | 64通过 | 历史scope→一次ScopedGuard→一次六结果；缺头原早返推进/Dispose及父事务可读；历史/版本/指纹/当前单位/正式收货/原.NET单位理由/日志排序 |
| 库存查询五方法 | 37 | 最终39通过 | 余额/流水快照和当前图片、软删/缺主数据、分页差异、字面/日期筛选、原反向与替换join、数量和序列双条件对账、实际约束 |

另受影响共享会话/命令专项33+12离线通过，合计122。首轮103真PG68通过35失败：Trace64全部通过；库存4通过35在共同Product夹具漏填SupplyType失败，均23502同一产品非空约束。源/PG必填依据已见[B20/前轮详情Manager](M18-详情Manager-主控验收.md)。只修新增InventoryFixture的Product为SupplyType="采购"，不改生产/断言/约束；仅库存39复验全部通过，未重跑未改变的Trace64。第一轮失败TRX完整保留。

实际命令：
```powershell
# 同进程清SWIFTEXPORT_RUN_PG_TESTS/FAILURE_TESTS/COMMAND_TESTS/RBAC_TESTS和RUN_SQL_SOURCE_READ_TESTS。
dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore -p:UseSharedCompilation=false --filter '(FullyQualifiedName~PostgreSqlCommandLockLifecycleTests|FullyQualifiedName~BusinessCommandExecutorTests|FullyQualifiedName~PostgreSqlCommandReceiptMigrationTests|FullyQualifiedName~PostgreSqlPurchaseOrderTraceMigrationTests|FullyQualifiedName~PostgreSqlInventoryQueryMigrationTests.Offline_)' --results-directory TestResults/postgresql-migration --logger 'trx;LogFileName=m15-session-m18-trace-m20-inventory-main-offline.trx' -v minimal
# 上述Receipt filter未匹配额外用例，实际122为40+37+33+12，不凭filter虚增。
# 同既有故障门禁：Process既有PG连接仅将Database改为dv_next_pg_failure_tests_20261004；不输出密码。
dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-build --no-restore --filter '(FullyQualifiedName~PostgreSqlPurchaseOrderTraceMigrationIntegrationTests|FullyQualifiedName~PostgreSqlInventoryQueryMigrationTests.RealPg_)' --results-directory TestResults/postgresql-migration --logger 'trx;LogFileName=m18-trace-m20-inventory-main-first-real-pg.trx' -v minimal
dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore -p:UseSharedCompilation=false --filter 'FullyQualifiedName~PostgreSqlInventoryQueryMigrationTests.RealPg_' --results-directory TestResults/postgresql-migration --logger 'trx;LogFileName=m20-inventory-main-legal-product-fixture-real-pg.trx' -v minimal
```

必要编译0错误，3既有NU1900和7既有MSTEST警告，无新增警告。固定故障库192.168.3.12:5432/dv_next_pg_failure_tests_20261004，全部独立随机单RC finally回滚，不消耗全局序列，前/首次后/复验后50表17595行和MD5一致，见[摘要](M18-Trace-M20-库存实库摘要.csv)。无DDL/初始化/清库/提交/备份修改，不需同步保留结构。沙箱首次复制新测试拒绝，窄授权后源和目标SHA逐项一致，若目标已有不同成果则拒绝覆盖；未回退任何改动。

固定SHA：
- Trace PG 627917C47D2D2B94166C834CDA407B6E2ABF0167A65C3091CAC82573B9104B28；离线579C2C8C22F12C3026F1342042266111245B00D0E3102EE4ADB6C3F1D1685D62；实库C90156355013AB4A573CC9053CAEC1B84064278334607BD50C9170B97053394C；A结果9150FE8F220A16B4F86850735F4A3B8950642182949082D738C2E57930984803。
- Inventory PG 99C5A28D84BA2E708FA7EC5CA68F91AB791E2BC4C28E4B5533B82602DCE5C6CF；离线466C00D52CC63742B549213D8DDC444EEAC28639E67172839F7121A775C8447B。
- Inventory实库接回2CD15D024494756CBB20A96DCDE2A103D03C4F12384FDD796701D5E99CB0F2CF，主控合法夹具后384E1244DFAC0BACCB911A75AD3096875EBAF723D52CBC3FA806DB60A140CD55。
- B结果84FB5075A3061C700990E905BBA53F227025F4DE28D1ABF41836CA9646EC1950。

仍待：服务器六语句中途真实跨连接变化、库存count/页物理跨连接屏障、服务器等待取消、DateTime.MaxValue与源溢出差异/性能、B10/B19完整Unicode。客户端reader切换/整批前同事务自写或LaterAppend两次查询不是这些并发证明。库存查询不调用M21过账，不把M21整个写模块当本仓储前置。

当前两窗口已实际续派：A M23-Q01-REVIEW01出库查询、B M21-R01-REVIEW01库存过账锁，只读审查结果/本人状态；wait_threads确认均active/inProgress。主控独占共享锁、实库及主清单，未新建窗口或worktree。目标仍active。

