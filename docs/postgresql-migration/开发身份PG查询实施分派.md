# P06-DU-IMPL01 开发身份 PG 窄查询

执行B：现有3557 worktree；主控负责公共接线及真实PG。主Agent Backend、后端API工作流，使用WebApi Auth/CurrentUser、Dapper、会话及质量技能。源码依据MAIN真实Development/DevUserMiddleware.cs和已交回P06宿主外直连审查2940C15C；技能仍写Middlewares路径/Controller直连的是旧描述，不照旧重建。

## 独占白名单

- 新增SwiftExport.Infrastructure/Repositories/Auth/Development/PostgreSqlDevUserQuery.cs；仅具体窄查询，不新Core接口或DI注册。
- 新增SwiftExport.Application.Tests/Database/PostgreSqlDevUserQueryMigrationTests.cs及.Integration.cs。
- 新增docs/postgresql-migration/任务结果/P06-开发身份PG查询-实施结果.md；本人执行B.md。

旧供应商完整服务测试与报告已STOPPED/completed固定SHA接回、主控24真实PG通过，从此只读。其他生产、Middleware、Program、项目、接口、公共、旧成果只读，不同步/改DI/连接数据库/运行种子/全量/Git/新代理或窗口。

## 保持的行为及必要差异

只交付可供未来PG中间件调用的具体FindUidAsync：现有scoped DbSession连接，UserName参数原值，不Trim/默认dev/改大小写；LIMIT1无ORDER，活动TRUE且未删（源NULL也未删，以COALESCE保留）。Uid标量可空，缺失NULL，错误传播。原查询无token、不传Transaction、不自建事务，保持调用形式，不抽象通用用户框架。

用户名等值沿当前PG Auth的rtrim ASCII尾空格+public.erp_dictionary_ci，复用元数据物理名/明确字符串参数；不新collation/DDL或硬编码Unicode样本。B10全域等价继续开放。原NOLOCK的脏读在PG不可实现，记录PG快照读取的技术差异；不降全服务隔离或加业务锁。

不写Middleware和缓存/claims：保留现有四条件启用、仅null回退dev、static Guid缓存/信号量、null不负缓存、四claims、异常阻断next及生产禁用。此交付不标记运行接入，主控P07完整切换时统一接入，避免当前SQL工厂混用PG语句。

## 验证与交付

清五DB运行开关，只必要编译及新相关离线；真实PG仅写编译。原生测试严格failure库，随机合法sys_user在父RC内种、finally回滚并精确UID确认零残留；不用既有管理员、不读写真实密码/角色/目录/Receipt。有效/停用/软删/缺失、空白原值、ASCII大小写/尾空格、参数/单行/错误传播按源码边界验证。不用模拟结果声称真实PG通过。

开始本人RUNNING，结束本人STOPPED；报告实际命令、文件SHA256、原源关键SHA起止、失败分类及未验证事项。需要其他文件先报告主控，不越界。无需跨树依赖同步；若编译缺真实必要依赖，报告路径及SHA，由主控停写后窄同步，不自行修公共。
