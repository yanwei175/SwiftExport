# M16-P03/S02 销售Detail主控切片验收

2026-10-05，主仓库HEAD为40d16632，新增成果未提交。主控在两执行窗口独立实施需求Trace期间，按销售源码及[冻结协议](../销售读取协议冻结.md)独占实现销售Detail和私有行事实。没有改Application业务服务/Core/DTO/API或运行DI，不推导全部销售查询已经迁完。

## 改动及依据

- 新增 `SwiftExport.Infrastructure/Repositories/SalesOrders/Query/PostgreSqlSalesOrderQueryRepository.cs`、`.LineFacts.cs`、`.Detail.cs`。不继承SQL Server通用仓储，不凑尚缺两个方法的完整接口。
- 新增 `SwiftExport.Application.Tests/Database/PostgreSqlSalesOrderDetailMigrationTests.Integration.cs` 和 `PostgreSqlSalesOrderDetailProtocolTests.Integration.cs`。真实SQL执行和命令边界观察，不是模拟数据库证明。
- 读取源SalesOrderQueryRepository/共用事实SQL、查询服务、保存及关闭事务调用位置、原SalesOrderManagerQuerySqlTests及实体/DTO/元数据；实际PG六表字段经只读information_schema核对。证据及范围见A原M16审查结果和主控冻结文件。

Candidate七字段→行事实SQL→当前有效头→当前有效行联冻结事实。JSON只传源工作集已物化记录，所有来源可信性和数量在SQL计算。SQL SourceVersion正且<=头Version，Fingerprint严格64位大写ASCII十六进制，源UUID事实选择顺序、原因优先级、三值逻辑、草稿0/Qty、确认未知、Posted净量、Reversed正历史、超发实际量保留。源只读常量探针额外确认64位大写通过、64位加尾空格/小写/63位拒绝；不是新增4个测试数量。

这是翻译SELECT INTO工作集及Npgsql整批隐式事务所需的基础设施实现，不新增业务规则。商业头映射、金额NULL回落、关闭信息、保存/关闭调用顺序原样保留。空头先缓冲第二集合，调用者拥有可空事务，不创建锁/事务/连接或临时表。

## 实际验证命令与结果

```powershell
dotnet build SwiftExport.Infrastructure/SwiftExport.Infrastructure.csproj --no-restore -p:UseSharedCompilation=false -v minimal
dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore -p:UseSharedCompilation=false --filter 'FullyQualifiedName~PostgreSqlSalesOrderDetailMigrationIntegrationTests' --results-directory TestResults/postgresql-migration --logger 'trx;LogFileName=m16-detail-facts-protocol-real-pg.trx' -v minimal
```

真实目标192.168.3.12:5432 / dv_next_pg_failure_tests_20261004，由主控串行；仅设置RUN_PG_FAILURE_TESTS=1，其他三个PG及SQL_SOURCE_READ开关关闭，凭据从获授权环境变量静默绑定。随机UID/商业键直接按已有元数据插入，事务ReadCommitted，finally回滚。实际锁等待测试另两连接短时局部表锁后回滚，不修改数据库结构或备份。

- 当前 **47通过/0失败/0跳过**（40事实+7协议）。局部测试必要编译0错误，完整分析时10条既有警告；最终后端阶段构建0错误/3既有NU1900。
- 真实事实：空头/软删/无行、草稿/确认、版本/指纹9分支、来源身份/状态12分支、Posted/Reversed/超发4分支、合法异常历史12分支、商业映射和未提交自写。
- 真实协议：明确命令阶段屏障下候选冻结、交付事实冻结而头行商业字段新值可见；事实后头软删仍缓冲第四阶段且外层事务可用；三个后续命令开始前取消；真正sales_order_line表锁阻塞中取消的无事务/已有事务两种路径。已有PG事务服务器取消后25P02由调用者回滚，仓储不暗自回滚/恢复；随后连接可复用、无临时表/关系/元组/advisory锁残留。
- 阶段变化由真实Npgsql命令包装器在下一命令前使用同一调用者事务自写，**不是跨连接业务写入并发验证**。真实等待取消包含独立锁连接，仅证明所述等待和资源边界。

## 首轮失败及修正保留

1. 首次局部源码编译1个CS9006（内插原始字符串正则量词大括号），下一轮6个CS1061（误用了元数据不存在的便捷属性）。只修正本次新增源码，沿用现有元数据及名字工具；之后编译0错误。未改公共映射。
2. 普通沙箱测试项目编译CS2012/MSB3101，WebApi既有obj产物写权限不足，归环境问题；按既有授权提升同一局部命令后正常构建，不改源码规避。
3. 首次真实40项为1通过/39失败，均新投影把PG date映射为DateOnly而DTO需要DateTime。修正为`::date::timestamp without time zone`，仍只截断日内时间、不时区换算；40项原断言复验全部通过，再补7协议得到47通过。

TRX依次为m16-detail-facts-first-real-pg.trx（保留39失败）、m16-detail-facts-fixed-real-pg.trx（40通过）、m16-detail-facts-protocol-real-pg.trx（当前47）。未删除有效断言、隐藏失败或运行全量测试。

50表行数/全字段摘要测试前后及两Trace验收后都完全相同，见M14-M16-主控实库摘要.csv。无DDL，因此无结构变更待同步dv_next；备份未写，数据库内容保留。

## 当前文件指纹及剩余

| 源码 | SHA256 |
|---|---|
| 主文件 | F73D3B017D4326D7D127469C02BAED71E83E1CCF286A9931C5B7914AA7C21E99 |
| Detail | 21D60B231986AF89CB8EBA9DF65851F230A5B8F038BC159BE5CBE1137DA9F135 |
| LineFacts | B379FBB461262888984813F4C04193FB8B222000302155708C1D730C33FD0AEB |

三文件已在A停写时固定SHA同步为后续Manager只读依赖，仍主控独占。A仅新增Manager partial，跨界报告后由主控处理。

尚未验证：销售Manager/Fulfillment、跨连接业务并发的RC/其他调用者隔离、完整Save/Close PG命令链、B10全字符比较及运行接线。活动来源重复受PG唯一索引禁止，本例合法两个Voided覆盖重复分支，不绕过结构造两个Active。数量<=0等非法历史仍保留SQL防线，未据此冒称合法实库读取通过。该切片通过不代表M16整体或迁移目标完成。
