# Guides Index

Guide 保存稳定规范和检查清单；Skill / Workflow 保存具体执行步骤。任务开始时仍先读 [总索引](../index.md)，再按需要读取本页条目。

## Backend

- [Application Service](backend/application-service.md) - 编排业务用例、事务边界和 Repository 调用时读取。
- [Dapper Repository](backend/dapper-repository.md) - 编写 SQL、仓储实现、分页查询、参数化和事务共享时读取。
- [Web API Controller](backend/webapi-controller.md) - 新增或调整 Controller、路由、请求/响应模型时读取。
- [Database Schema](backend/database-schema.md) - 涉及表结构、字段、审计、软删除、迁移风险时读取。

## Shared

- [DTO](shared/dto.md) - 修改 DTO、请求响应模型、Entity/LineModel 映射时读取。
- [Mapster Mapping](shared/mapster-mapping.md) - 处理 Entity、DTO、前端 Model/LineModel、Request/Response 映射时读取。
- [Refit API](shared/refit-api.md) - 修改 Contracts Refit 接口、路由、请求/响应契约时读取。
- [Service Result](shared/service-result.md) - 处理统一返回、错误消息、业务失败和异常边界时读取。

## Frontend WPF

- [WPF MVVMLight](frontend-wpf/wpf-mvvmlight.md) - 维护 .NET Framework WPF / MVVMLight 页面时读取。
- [WPF CommunityToolkit MVVM](frontend-wpf/wpf-communitytoolkit-mvvm.md) - 设计未来 .NET 10 WPF、CommunityToolkit、前端基类、Source Generator、导航、弹窗和消息基础设施时读取。
- [WPF DataGrid](frontend-wpf/wpf-datagrid.md) - 处理 ERP 可编辑表格、LineModel、批量保存和脏数据时读取。
- [WPF Dialog Navigation](frontend-wpf/wpf-dialog-navigation.md) - 处理消息、确认框、模态窗口、导航和未保存变更时读取。
- [WPF UI Services](frontend-wpf/wpf-ui-services.md) - 处理 `IDialogService`、DialogHost、消息提示、路由服务和 ViewModel/UI 服务边界时读取。
- [WPF Pagination](frontend-wpf/wpf-pagination.md) - 处理分页控件、分页命令、PageIndex/PageSize 和分页前未保存变更拦截时读取。
- [WPF Image Cell](frontend-wpf/wpf-image-cell.md) - 处理 DataGrid 图片列、图片转换器、缩略图和 ToolTip 预览时读取。
- [WPF Theme Resource](frontend-wpf/wpf-theme-resource.md) - 调整主题资源、样式、资源字典和控件视觉时读取。
- [.NET 10 WPF Official Theme Style](frontend-wpf/wpf-official-theme-style-guide.md) - 设计新 .NET 10 WPF 客户端主题、官方 Fluent、命名 Style 和避免覆盖默认控件样式时读取。
- [.NET 10 WPF Fluent Layout](frontend-wpf/wpf-fluent-layout-guide.md) - 设计新 .NET 10 WPF 页面布局、4px 间距系统、表单、卡片、DataGrid 页面和 XAML 布局规范时读取。

## Workflow

- [Safe Refactoring](workflow/safe-refactoring.md) - 大文件、历史代码、模块拆分和风险重构时读取。
- [Codex Task Writing](workflow/codex-task-writing.md) - 编写给 Codex 的任务、约束、验收标准时读取。
