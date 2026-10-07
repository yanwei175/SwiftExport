# PostgreSQL 当前基线

核对日期：2026-10-07。用于后端实现、数据库模型规划及相关代码审查。这里只维护当前接线、源码入口与历史资料边界，SQL、事务、映射和验证细则继续归现有专项 Skill；不新增一套提供者切换工作流。

## 当前事实与目标

- 当前运行栈是 PostgreSQL + Npgsql + Dapper，SQL Server 旧运行实现及依赖已退出。不恢复旧接线，不建立长期双库支持。
- Infrastructure DI 注册 `PostgreSqlConnectionFactory`、`PostgreSqlExceptionClassifier`、`PostgreSqlDapperRepository<>` 和各 PG 专用仓储。Core `IDbSession` 与仓储 `DbSession` 解析为同一个 scoped 对象；Application 使用 Core 契约，最外层用例拥有事务。
- 实体已直接声明 PostgreSQL 物理名称、类型及结构表达式；仓储与生成器共用 `EntityMetadataCache`。保留 CLR 参数名、DTO 结果别名和元数据是 Dapper 映射职责，不表示还需兼容 SQL Server。
- 已切换数据库不表示任意业务组合、运行库写入或并发场景均已验收。后续按用户当前任务和实际风险选择验证，不从旧清单自动重启全盘迁移或 Review。

## 源码入口

| 对象 | 当前源码 |
| --- | --- |
| 运行接线 | [Infrastructure DI](../../SwiftExport.Infrastructure/DependencyInjection.cs) |
| 连接与会话 | [连接工厂](../../SwiftExport.Infrastructure/Data/PostgreSqlConnectionFactory.cs)、[DbSession](../../SwiftExport.Infrastructure/Data/DbSession.cs) |
| 通用仓储与映射 | [PG 仓储](../../SwiftExport.Infrastructure/Repositories/Base/PostgreSqlDapperRepository.cs)、[元数据缓存](../../SwiftExport.Infrastructure/Repositories/Base/EntityMetadataCache.cs)、[参数绑定](../../SwiftExport.Infrastructure/Repositories/Base/PostgreSqlParameters.cs) |
| 数据库结构 | [Core 元数据](../../SwiftExport.Core/DatabaseSchema/EntitySchemaMetadata.cs)、[PG 生成器](../../SwiftExport.Infrastructure/DatabaseSchema/PostgreSqlSchemaGenerator.cs) |
| 结构评审基线 | [生成 SQL](../../SwiftExport.Infrastructure/Database/Generated/SwiftExport.PostgreSql.Schema.preview.sql) |
| 权限目录 | [代码目录](../../SwiftExport.Contracts/DTOs/Auth/Permissions/PermissionCatalog.cs)、[显式同步入口](../../_tools/DbSeed/同步权限目录.ps1) |

实际调用方、接口与 DI 优先于旧类名、聊天总结或历史阶段描述。源码和现行规则不一致时说明偏差，不据文档直接推断部署或测试已通过。

## 按任务读取现有 Skill

- SQL、专用仓储：`dapper-repository`；通用 CRUD、并发版本和映射：`infrastructure-repository-base`。
- 连接与共享事务：`infrastructure-db-session`；服务事务边界：`application-service-context`；DI：`infrastructure-di-registration`。
- 结构规划：`data-model-governance`；验证范围：`backend-quality-gates`。

以上入口均在 `.codex/skills/aspdotnet10`，由该组 `index/SKILL.md` 按任务选择；不为读取本基线展开所有 Skill。

## 工具、部署与验证

结构生成器只生成或核对本地文件，不自动部署。修改实体结构后运行现有 `_tools/SchemaGenerator` 并同步生成基线；已有库的升级须有单独增量脚本及明确部署范围，不对运行库重跑空库初始化脚本。

权限目录首次初始化及代码目录变更后使用显式同步入口，复用已有 WebApi 连接配置。该入口不创建账号、不授予角色权限、不迁移结构；默认无参数的 DbSeed 管理员引导不是权限同步命令。具体使用及实证见 [同步说明](../../_tools/DbSeed/权限目录同步说明.md)。

数据库读取默认只读，写入沿用用户已明确授权的实例、库名和范围；测试库授权不转为运行库授权，备份库仍受保护。锁、事务、版本、方言、约束变化按风险做真实 PG 定向验证，模拟测试不能替代；不得多个窗口同时重置共享故障测试库。具体时间控制及失败分类沿用 [测试与验收总纲](测试与验收总纲.md)。

## 历史资料与旧窗口

`docs/postgresql-migration` 保留历史清单、失败和实证，不是日常开发的默认执行计划。旧窗口恢复时先核对本基线、当前 AGENTS 与源码，不能从旧上下文继续已退役阶段。已完成窗口可归档保留记录；聊天归档不等于工作成果已合并，也不等于获准删除 worktree 或未提交文件。

## 本次核对范围

本次依据上述 DI、连接工厂、会话上下文、元数据缓存、实体和工具源码核对治理入口。6 个角色、12 条路由的 TOML、角色/路由唯一性、文件引用及原工作流归属检查通过；修改文档的链接、编码和 diff 检查通过。官方 Skill 校验器因缺少 PyYAML 未成功运行，采用标准库检查本次四个入口的单行 name/description、名称约束和占位符；不将此有限校验称为通用 YAML 或行为验收。

本次仅修改治理文档及数据库 README，没有修改业务代码、测试、项目文件、结构脚本或运行配置；未重新构建、执行业务测试或访问数据库。历史验收记录保留原日期、覆盖范围和限制，不因本次整理扩大完成声明。
