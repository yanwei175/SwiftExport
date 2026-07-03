# WPF CommunityToolkit MVVM Guide

## 适用场景

用于未来 SwiftExport .NET 10 WPF 前端的页面、ViewModel、行模型、集合模型、导航、弹窗、消息和 DataGrid 基础设计。

当前 .NET Framework 4.8 + MVVMLight 页面不使用本 Guide；维护旧前端时读取 [WPF MVVMLight](wpf-mvvmlight.md)。

## SwiftExport 规则

- 新 .NET 10 WPF 前端使用 `CommunityToolkit.Mvvm`，不混用 MVVMLight。
- 页面 ViewModel 优先继承未来 `ViewModelBaseEx`，承载 Busy、只读保护、异常包装、命令刷新和页面级状态。
- 行模型优先继承 `BaseLineModel<TDto>`，不要继承页面级 ViewModel 基类。
- 行集合优先继承 `BaseListModel<TLine,TDto>`，统一管理删除池、变更集、批量勾选、验证和提交/回滚。
- 可编辑 DataGrid 不直接绑定 DTO。
- ViewModel 调用 Refit API 和前端 UI Service，不直接访问 Repository、SQL、Controller 或 Window。
- 属性优先用 `[ObservableProperty]`，命令优先用 `[RelayCommand]`。
- 异步命令返回 `Task`，业务命令不要写成 `async void`。
- 构造函数只做依赖保存、默认值和事件订阅，不执行大量异步加载。
- 保存成功后必须清理页面和行集合脏状态。
- 删除已持久化行必须进入删除池；删除新增行可以直接移除。

## 基类职责

`ObservableObjectEx`：

- 属性通知基础。
- `SuppressScope` / `IsSuppressed`。
- 不包含 Busy、Dialog、Navigation、Message、Repository。

`ViewModelBaseEx`：

- `Title`、`BusyMessage`、`ValidationSummary`。
- `IsBusy`、`IsNotBusy`、`IsReadOnly`、`CanWrite`。
- `RunBusyAsync`、`RunWriteAsync`。
- 异常处理和只读违规处理入口。

`BaseLineModel<TDto>`：

- `Uid`、`Version`、行状态、脏数据。
- `IsChecked`、`IsSelected`、`IsReadOnly`。
- DTO 加载、DTO 导出、验证、删除恢复、提交和回滚。

`BaseListModel<TLine,TDto>`：

- `Items`、内部完整集合、`SelectedItem`。
- 删除池、批量勾选、变更集、列表级验证。
- `AcceptChanges`、`RejectChanges`、`Reindex`。

## UI Service 规则

- `INavigationService` 只负责 route 到 ViewModel 的页面切换。
- `IDialogService` 负责阻塞式确认、错误详情、文件选择和编辑弹窗。
- `IMessageService` 负责非阻塞提示，例如保存成功和状态栏消息。
- `IStatusMessageService` 负责 Shell 状态绑定。
- ViewModel 不直接 `MessageBox.Show`。
- ViewModel 不直接 `new Window()`、`new Page()`。

## DataGrid 规则

推荐流程：

```text
Contracts DTO
  -> LineModel
  -> BaseListModel
  -> DataGrid
  -> GridChangeSet<TDto>
  -> Request DTO
  -> Refit API
```

保存流程：

1. `Lines.ValidateAllAsync()`。
2. `Lines.GetChangeSet()`。
3. ViewModel 适配为具体 Request。
4. 调用 Refit API。
5. 使用后端返回结果重载，或调用 `Lines.AcceptChanges()`。
6. 调用页面 `AcceptChanges()`。

取消编辑：

1. `Lines.RejectChanges()`。
2. 页面 `RejectChanges()`。
3. 清理 `ValidationSummary`。

## 分页规则

分页控件优先使用命令驱动：

- `PageIndex`、`PageSize` 单向绑定。
- 控件通过 `PageChangeCommand` 发送请求。
- ViewModel 先处理未保存变更拦截。
- 只有 ViewModel 确认翻页后才更新分页属性并查询。

## Mapster 规则

- DTO 与 LineModel 转换优先用 Mapster。
- 超过 10 个字段的手工复制应改为 Mapster 或明确 Mapping 类。
- 复杂映射统一放在 Mapping 区域。
- 不在 Repository 中写 Mapster 配置。
- Dapper 直接返回 DTO 时，不需要再 Mapster。

## 修改前检查

- 当前任务是新 .NET 10 前端，还是旧 .NET Framework 4.8 前端。
- 是否需要保持旧 Binding 名称和 UI 行为。
- 是否已有 `BaseLineModel`、`BaseListModel`、Dialog、Message、Navigation 可复用。
- 是否影响 Refit API、DTO、Mapster 映射或页面路由。
- 是否需要先建立基础设施，再迁移具体页面。

## 禁止事项

- 禁止在旧 MVVMLight 项目中局部混用 CommunityToolkit Source Generator。
- 禁止 ViewModel 直接写 SQL 或调用 Repository。
- 禁止 ViewModel 直接创建窗口或页面。
- 禁止把业务保存流程塞进 `BaseLineModel<TDto>` 或 `BaseListModel<TLine,TDto>`。
- 禁止让 `BaseListModel` 直接依赖后端请求 DTO。
- 禁止把未保存变更拦截藏在分页控件内部。
