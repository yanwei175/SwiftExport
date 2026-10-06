# M16-S03-IMPL-01 销售履约实施结果

执行B：`01a10790-0ef8-7663-ab9b-aeee9f7f1c0d`，工作目录 `C:/Users/BigBigBigDv/.codex/worktrees/3557/SwiftExport`。2026-10-05 04:47:43 +08:00 开始；本轮交付后停写。主 Agent Backend，后端API工作流；依据主仓库《销售履约实施分派》《销售读取协议冻结》、实际源Fulfillment/共用事实SQL、DTO、接口、服务及相关测试实施。此前已读路由、Dapper、会话、仓储、组织和质量技能按同版本去重复用。

## 授权与实际改动

只新增三个源码/测试文件、本结果及本人状态；三销售公共private文件只读，没有接管A的Manager、修改旧测试、Core/Contracts/Application/DI。本轮新增业务读取实现，但没有改变业务目标口径、接完整接口或运行接线。没有数据库连接/DDL/初始化清理、全量构建/测试、Git命令、提交推送、其他聊天消息或代理。

新增文件绝对路径与固定SHA256：

| 文件 | SHA256 |
| --- | --- |
| `C:/Users/BigBigBigDv/.codex/worktrees/3557/SwiftExport/SwiftExport.Infrastructure/Repositories/SalesOrders/Query/PostgreSqlSalesOrderQueryRepository.Fulfillment.cs` | `D4B7C3FF11BD69F6C42E0C2C1845306384758AF3A88699C1461C9AEBBA20D6CA` |
| `C:/Users/BigBigBigDv/.codex/worktrees/3557/SwiftExport/SwiftExport.Application.Tests/Database/PostgreSqlSalesOrderFulfillmentMigrationTests.cs` | `DA05ABD97A779AC7DC5FFEDF3702BFC922264766D0C0D76C5CC9960DBFDC2207` |
| `C:/Users/BigBigBigDv/.codex/worktrees/3557/SwiftExport/SwiftExport.Application.Tests/Database/PostgreSqlSalesOrderFulfillmentMigrationTests.Integration.cs` | `45E77434366534FD599F079A9AF5B2154A260C51C67F88363D28B3A871C23F40` |

## 实现与假设

依据冻结协议明确采用七阶段：候选七字段→一次行事实→当前真实头/客户→Gross重新选择并完整缓冲履约行→空头分支→采购/最新报告→箱单历史→异常行ID→原C#按行装配。其中头/履约行不补源没有的软删除过滤，第二次Gross选择不是第一次实体缓存。冻结关系只携带源候选、事实和履约临时字段；只借用可空事务和取消令牌，不创建临时表或生产锁，也不结束调用方事务/连接。

采购SQL保留整采购行Quantity和最新反馈，不分摊到销售量；当前Product.Unit优先，空值/空ASCII串仅根Net回落Gross单位，不读取Net.UnitSnapshot补证据。源采购物理QTY在PG映射中为`quantity`，已按现有映射及已验收Trace实现核对，不直接沿用`qty`。客户异常原因优先于单位异常；活动Link的版本/指纹/生命周期/目标异常另查，不能被采购过滤误判为未关联。

所有DTO投影quoted CLR别名，数值由SQL计算；NULL数量、墙上时间、SourceVersion DESC NULLS LAST、源SqlGuid升降序及ASCII修剪/字典比较保留。Posted/Reversed都展示历史，Posted才贡献净量；超发继续关联Gross，关闭允许继续交付为0，未知量仍NULL。SourceState/PurchaseReviewReason/PurchaseState保持原优先级；重复销售行仍由ToDictionary抛异常，不合并坏数据。未新增入库Guard，源履约没有此依赖。

源Dapper默认忽略DBNull字符串赋值，因此NULL GrossDemandUnitSnapshot在公开DTO仍保留初始化空串；没有修改全局ApplyNullValues。关系尾查询只使用SalesOrderLineId/GrossDemandId，完整十六源临时列冻结不包括采购/历史集合或装配状态；这不是对未知数量置零。

## 实际验证

实际运行三次相同局部命令：

```powershell
@('SWIFTEXPORT_RUN_PG_TESTS','SWIFTEXPORT_RUN_PG_FAILURE_TESTS','SWIFTEXPORT_RUN_PG_COMMAND_TESTS','SWIFTEXPORT_RUN_PG_RBAC_TESTS','SWIFTEXPORT_RUN_SQL_SOURCE_READ_TESTS') | ForEach-Object { Remove-Item "Env:$_" -ErrorAction SilentlyContinue }
dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore -p:UseSharedCompilation=false --filter 'FullyQualifiedName~PostgreSqlSalesOrderFulfillmentMigrationTests.Offline_' --verbosity minimal
```

首轮23通过、3失败、0跳过：两例SQL检查预期用了不存在的`packing_history`别名，另一例错误期望Dapper把NULL字符串覆盖DTO空串初始化。改为实际`invalid_history_count`和源Dapper默认映射预期，没有修改生产行为或删除数量/事务/取消断言。第二轮26通过、0失败、0跳过；当轮编译出现本轮一条日期断言参数分析器警告，改为明确expected日期变量。最终26通过、0失败、0跳过，必要依赖和两新测试文件编译成功，0编译错误，仅7条既有其他测试警告；本轮无新警告。末次编译/测试后只清理离线新文件两处空行尾空格，最终SHA以上表为准。

离线均经公开GetFulfillmentAsync与真实Dapper/ADO路径：可空事务/同一取消令牌透传、七阶段数据/参数、七字段/九字段/十六字段JSON、NULL头先缓冲行后停止尾读取、行阶段失败、重复行Dictionary失败、多行和无关证据路由、采购/历史所有DTO列、原状态优先级、未知量NULL、空Uid与预取消，以及每一阶段执行失败/等待取消共14例。脚本不解释SQL，不声明真实PG商业聚合已通过。

三个新增文件按从无到有的完整内容审查，核对目标列、JOIN边界和源条件；UTF-8无替换字符，尾空格检查均0。没有调用Git检查或整理其他既有未提交成果。

## 真实PG交付与未验范围

真实PG **40个数据用例仅编写和编译，未运行**。严格既有GetFailureConnectionString门禁、DoNotParallelize、随机UID、ReadCommitted事务、finally回滚；不绕CHECK、唯一索引或不可变链接触发器。

覆盖空头/空行/草稿/确认未知、商业NULL和墙上时间、Posted/Reversed/超发/关闭、坏来源与历史、多行同采购行路由/共享/整行数量及最新报告、当前产品单位/根回落/子禁止回落/根子聚合、一项坏单位令整组未知、采购客户原因、合法已释放取消Link、8种活动坏证据、当前头/行软删与Gross重新选择、无外层事务/连接可复用、7阶段真实驱动取消与调用方回滚恢复。阶段包装器仅在测试命令边界执行同事务精确UID变更或真实只读等待，不增加生产回调，不模拟数据库结果；同事务场景不声称跨连接一致性通过。

最新反馈Sequence并列受现有`UX_SupplierProgress_Line_Sequence`禁止，未绕约束造数。生产仍保留Sequence DESC及源SqlGuid Uid DESC；实际入口离线命令检查该尾键。真实并列选择取证须主控安排合法来源协议/探针，不能算本轮实库通过。

未验证：真实PG SQL解析/聚合/约束/映射和取消恢复、跨连接提交协议、完整三方法接口/DI、保存/关闭服务链、较大回归。Unicode字典全字符差异B10继续开放，未扩大本切片文本治理或新增兼容框架。所有实际数据库验收由主控串行排单。

## 三只读前置SHA

冻结分派SHA与结束读取SHA完全一致：

| 文件 | 前后SHA256 |
| --- | --- |
| `PostgreSqlSalesOrderQueryRepository.cs` | `F73D3B017D4326D7D127469C02BAED71E83E1CCF286A9931C5B7914AA7C21E99` |
| `PostgreSqlSalesOrderQueryRepository.Detail.cs` | `21D60B231986AF89CB8EBA9DF65851F230A5B8F038BC159BE5CBE1137DA9F135` |
| `PostgreSqlSalesOrderQueryRepository.LineFacts.cs` | `B379FBB461262888984813F4C04193FB8B222000302155708C1D730C33FD0AEB` |

结果写入后更新本人状态STOPPED，再聊天交付；只按上述精确文件和固定SHA接回，不整体同步worktree。
