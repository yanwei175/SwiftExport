# SwiftExport AI Wiki Index

这是 SwiftExport 项目的 AI 工作流入口。Codex 接到任务后先读本页，再按任务类型读取对应 `wiki/` 页面和源码事实。

索引采用“链接 + 简短说明”的形式：链接用于快速跳转，说明用于判断什么时候读取。

## 默认读取顺序

1. 先读本页。
2. 按任务类型读取下方链接的 skill、workflow、guide 或项目资料。
3. 再读取源码中的真实实现。
4. 最后制定修改方案或改代码。

新增 skill、workflow 或项目资料时，只更新本页、对应子索引和 `log.md`，不要为了新增路由修改 `AGENTS.md`。

## 快速路由

- [项目上下文摘要](project/context-summary.md) - 需要快速恢复当前 AI Wiki 分层、项目边界、入口和工作流规则时读取。
- [项目地图](project/project-map.md) - 需要定位项目结构、入口文件、分层职责、构建命令时读取。
- [ERP 业务总纲](project/erp-business-blueprint.md) - 涉及业务流程、需求、采购、入库、出库、PackingList、客供件或状态回写时读取。
- [项目接手说明](project/onboarding.md) - 接手新功能、不熟悉模块、需要打开顺序和风险提示时读取。
- [Skills Index](skills/index.md) - 需要选择正式 skill 时读取。
- [Workflows Index](workflows/index.md) - 需要选择跨文件或跨层工作流时读取。
- [Guides Index](guides/index.md) - 需要查看稳定规范、检查清单、实现细节参考时读取。
- [工作日志](log.md) - 需要了解 AI Wiki 规则变更、ingest 记录、资料整理历史时读取。

## 后端 / Dapper / Repository

- [DbSession 与 RepositoryBaseContext](skills/backend/db-session-context.md) - 涉及连接、事务、Repository 构造、多个仓储共用事务时读取。
- [Dapper Repository](skills/backend/dapper-repository.md) - 涉及 Repository、SQL、分页、批量保存、DI 注册、Core Interface 时读取。
- [Application Service Guide](guides/backend/application-service.md) - 涉及业务编排、事务边界、服务层调用仓储时读取。
- [Dapper Repository Guide](guides/backend/dapper-repository.md) - 需要更细的仓储实现检查清单时读取。
- [WebApi Controller Guide](guides/backend/webapi-controller.md) - 涉及 Controller 路由、入参、返回结构、Service 调用边界时读取。
- [Database Schema Guide](guides/backend/database-schema.md) - 涉及表结构、字段、审计列、软删除、SQL 字段映射时读取。

## 前后端契约 / Contracts / Refit

- [Refit Contract Change](skills/shared/refit-contract-change.md) - 涉及 DTO、Refit API、Controller 路由、接口返回结构同步时读取。
- [DTO Guide](guides/shared/dto.md) - 设计或修改 Contracts DTO、Request、Response 时读取。
- [Mapster Mapping Guide](guides/shared/mapster-mapping.md) - 涉及 Entity、DTO、前端 Model/LineModel、Request/Response 映射时读取。
- [Refit API Guide](guides/shared/refit-api.md) - 设计或修改 Refit Interface、HTTP 方法和路由时读取。
- [ServiceResult Guide](guides/shared/service-result.md) - 涉及统一返回、错误处理、前端解析时读取。

## 前端 / WPF / VSTO

- [WPF / VSTO 前端工作流](workflows/frontend/wpf-vsto-workflow.md) - 涉及 `SwiftExport.View.FW`、`SwiftExport.VSTO`、ViewModel、XAML、TaskPane、前端 API 调用时读取。
- [WPF DataGrid Editing](skills/frontend-wpf/wpf-datagrid-editing.md) - 涉及 DataGrid 编辑列、行模型、脏数据、批量保存、只读状态时读取。
- [WPF Theme Fix](skills/frontend-wpf/wpf-theme-fix.md) - 涉及 ResourceDictionary、样式、主题、DataGrid 视觉修复时读取。
- [MVVMLight Guide](guides/frontend-wpf/wpf-mvvmlight.md) - 涉及 ViewModel、命令、属性通知、消息通信时读取。
- [WPF DataGrid Guide](guides/frontend-wpf/wpf-datagrid.md) - 涉及 DataGrid 结构、列绑定、编辑行为、行状态时读取。
- [Dialog / Navigation Guide](guides/frontend-wpf/wpf-dialog-navigation.md) - 涉及弹窗、导航、页面切换、宿主边界时读取。
- [WPF UI Services Guide](guides/frontend-wpf/wpf-ui-services.md) - 涉及 `IDialogService`、DialogHost、消息提示、路由服务时读取。
- [WPF Pagination Guide](guides/frontend-wpf/wpf-pagination.md) - 涉及分页控件、分页命令和分页前未保存变更拦截时读取。
- [WPF Image Cell Guide](guides/frontend-wpf/wpf-image-cell.md) - 涉及 DataGrid 图片列、图片路径转换器、缩略图和预览时读取。
- [Theme / Resource Guide](guides/frontend-wpf/wpf-theme-resource.md) - 涉及资源字典、主题键、控件样式、颜色资源时读取。

## 重构 / 项目接手 / 任务书写

- [Safe Refactoring Workflow](workflows/safe-refactoring.md) - 涉及大范围重构、迁移、拆分、行为保持、高风险修改时读取。
- [Safe Refactoring Guide](guides/workflow/safe-refactoring.md) - 需要重构检查清单、阶段拆分、验证策略时读取。
- [Codex Task Writing Guide](guides/workflow/codex-task-writing.md) - 需要把需求整理成 Codex 可执行任务书时读取。

## 风险提示

- 本仓库包含 .NET Framework WPF/VSTO 项目，完整构建依赖本机 Visual Studio / MSBuild 工作负载。
- `SwiftExport.View.FW/DI/DependencyInjection.Apis.cs` 中前端 API BaseUrl 仍是本地开发硬编码。
- 配置或工具文件中可能存在本地连接字符串，回复和文档中不要复制敏感值。
- 当前未发现独立自动化测试项目，较大业务改动应优先运行相关构建并做人工验证说明。
