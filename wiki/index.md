# SwiftExport AI Wiki Index

这是 SwiftExport 项目的 AI 工作流入口。Codex 接到任务后先读本页，再按任务类型读取对应 `raw/` 文件和源码。

## 任务路由

新增 skill、workflow 或项目文档时，只更新本页索引和 `wiki/log.md`，不要为了新增路由修改 `AGENTS.md`。

### 后端 / Dapper / Repository

涉及 Repository、Dapper、SQL、事务、`RepositoryBaseContext`、`DbSession`、Infrastructure 仓储、Core Repository Interface 时读取：

- `raw/skills/backend/db-session-context/SKILL.md`
- `raw/skills/backend/dapper-repository/SKILL.md`

### 前端 / WPF / VSTO

涉及 WPF 页面、ViewModel、前端 Model、Refit API 调用、Excel VSTO 承载、TaskPane 或前端 DI 时读取：

- `raw/workflows/frontend/wpf-vsto-workflow.md`

### 项目地图 / 入门 / 架构查询

涉及项目结构、入口文件、构建命令、风险说明时读取：

- `raw/project/project-map.md`
- `raw/project/onboarding.md`

## 项目总览

- `raw/project/project-map.md` - 项目结构、入口文件、分层说明、前后端目录地图；处理架构查询、定位文件、理解模块时读取。
- `raw/project/onboarding.md` - 新任务入门、常用打开顺序、构建运行命令、当前风险；接手新功能或不熟悉模块时读取。

## 后端 Skills

- `raw/skills/backend/db-session-context/SKILL.md` - `RepositoryBaseContext`、`DbSession`、连接和事务规范；涉及连接、事务、Repository 构造时读取。
- `raw/skills/backend/dapper-repository/SKILL.md` - Dapper Repository 开发规范；涉及 Repository、SQL、分页、批量保存、DI 注册时读取。

## 前端 Workflows

- `raw/workflows/frontend/wpf-vsto-workflow.md` - WPF / VSTO 前端开发工作流；涉及 `SwiftExport.View.FW`、`SwiftExport.VSTO`、前端 DTO/API、ViewModel、XAML 页面时读取。

## 历史资料

- `docs/ai/PROJECT_MAP.md` - AI 早期生成的项目地图，保留作历史资料；如与 `raw/project/project-map.md` 或源码冲突，以源码和 raw 当前版本为准。
- `docs/ai/ONBOARDING_REPORT.md` - AI 早期生成的入门报告，保留作历史资料；如与 `raw/project/onboarding.md` 或源码冲突，以源码和 raw 当前版本为准。
- `.codex/skills/backend/**` - 旧 Codex skill 位置，已迁移到 `raw/skills/backend/**`；后续以 raw 为权威。

## 风险提示

- 本仓库包含 .NET Framework WPF/VSTO 项目，完整构建依赖本机 Visual Studio / MSBuild 工作负载。
- `SwiftExport.View.FW/DI/DependencyInjection.Apis.cs` 中前端 API BaseUrl 仍是本地开发硬编码。
- 配置或工具文件中可能存在本地连接字符串，回复和文档中不要复制敏感值。
- 当前未发现独立自动化测试项目，较大业务改动应优先运行相关构建并做人工验证说明。
