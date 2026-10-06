# M26-S-IMPL-01 Excel 销售导入读取

2026-10-05，执行 B 的 M13-Q 已停止并由主控精确接回；本轮只迁三个销售导入读取方法。主 Agent 为 backend，沿用后端 API 工作流及 Dapper 技能；生产服务、接口和公共实现只读。

## 独占文件

- SwiftExport.Infrastructure/Repositories/ForExcel/PostgreSqlForExcelSalesOrderRepository.cs
- SwiftExport.Application.Tests/Database/PostgreSqlForExcelSalesMigrationTests.cs
- SwiftExport.Application.Tests/Database/PostgreSqlForExcelSalesMigrationTests.Integration.cs
- 本 worktree 的 docs/postgresql-migration/任务结果/M26-Excel销售读取-结果.md，以及本人执行 B 状态页。

不修改采购导入仓储（HOLDLOCK 的事务/空范围协议尚未冻结）、任何既有服务、Core/Contracts、DI、映射、共享测试夹具或 DDL。保留前轮成果，不整体同步旧 worktree。需要跨边界先报主控。

## 冻结调用与语义

ForExcelSalesOrderService.CheckAsync 无自建事务；PackingListImportChecker 在检查及事务内保存复用 MatchProductsAsync。允许外层事务为 null；有事务全部命令透传同一事务与取消令牌，仓储不自建/提交/回滚事务。原查询没有 HOLDLOCK，不擅自增加锁或资格校验。

FindCustomersAsync 保持参数 Trim().ToUpperInvariant()、数据库仅去 ASCII 首尾空格后 UPPER 的精确匹配、未删过滤；停用与多候选必须返回。MatchProductsAsync 空数组直接空结果；数组由调用方规范化，不二次处理。LEFT JOIN 产品只排软删，保留断链/停用候选。IsUsable 只沿用关系启用、产品启用及单位 ASCII 修剪后非空，不引入采购供货/物料类型条件。LinkSku 沿用客户修剪非空优先，否则产品原值，所有 NULL 投影按源保留。

FindExistingOrdersAsync 两编号均 .NET 空白时直接空结果；否则各自 Trim().ToUpperInvariant()，同客户、未删、任一非空编号相等。返回所有匹配，不把编号当唯一键，不添加状态条件。使用 PG 已冻结映射及 public.erp_dictionary_ci，保留稳定排序，明确 PG UUID 次序与 SQL Server 次序已有差异边界；不要声称跨库逐行次序完全一致。IN 数组转 ANY 参数，不拼业务输入；必要显式类型及别名，不采用 SELECT *。

## 验收及协作

可与 A 的 RBAC 独占文件并行，前置 P02/P03/M01/M04 接口与投影已冻结。仅本销售读取切片放行，M26 采购及完整单据服务集成仍由主控后续串行安排。

执行 B 必要编译、独立离线真实 Dapper 映射/参数/事务/取消场景及现有 ForExcelSalesOrderTests/ContractTests、PackingListImportAndAssociationTests 定向验证。真实 PG 只编写不执行：固定现有故障测试库入口，随机实体，优先单事务回滚，涵盖三个方法、大小写/空格/特殊字符、断链、歧义、LinkSku、软删除、启用、单位、空数组、两编号 OR 及空值。不得初始化/重置数据库。有关 Unicode/排序差异实测后登记，不静默放宽断言。

开始先更新本人状态运行中；结束先写结果与实际命令、未验证事项，再写运行完毕停写，最后聊天交付。主控负责合并、真实 PG 串行验证及主清单。无 Git 提交/推送/部署。

## M26-S-FIX-01 补充冻结

首轮停写后主控预审并只读常量核实：SQL Server `UPPER(LTRIM(RTRIM(N'PGKEY'))) COLLATE Chinese_PRC_CI_AS=N'pgkey '` 为真，前导空格 `N' pgkey'` 为假。原数组不二次规范化是参数契约，但 SQL Server 等值自身忽略尾 ASCII 空格，PG 裸 ANY 不等价。B 新轮仅原五文件，在 SQL 内对 unnest 数组项忽略尾 ASCII 空格，保留原 C# 数组、前导/Tab/Unicode/内部空格和 NULL，不把错误的“尾空格不匹配”测试留作业务规则。真实测试仍由主控串行执行。

本轮附加只读审查 ExecutionEligibilityService 与原授权 HOLDLOCK：该 Serializable 服务在事务内随机生成审计 ID、未准备 PG 日志租约，且需与 RBAC 撤权及目录同步协作。只记录阻塞和最小建议，尚未放行该服务/仓储实现，公共协议主控持有。
