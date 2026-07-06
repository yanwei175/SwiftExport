# .NET 10 前端 MVVM 基础设计

本文是 SwiftExport 当前 .NET 10 WPF 前端主线的 MVVM 基础设施总纲。旧 .NET Framework 4.8 + MVVMLight + VSTO 前端只作为兼容维护对象，不再是默认前端开发目标。

来源参考：

- `raw/wpf-communitytoolkit-mvvm.md`
- `raw/wpf-mvvm-template.md`
- `raw/wpf-navigation-service.md`
- `raw/wpf-dialog-service.md`
- `raw/wpf-message-service.md`
- `raw/wpf-designer-page-navigator.md`
- `raw/wpf-designer-Image.md`
- `raw/mapster-mapping.md`

## 1. 技术栈默认选择

当前 .NET 10 前端默认使用：

- .NET 10 WPF。
- `CommunityToolkit.Mvvm`。
- `Microsoft.Extensions.DependencyInjection`。
- `Refit` 调用 `SwiftExport.Contracts/Apis/**`。
- `Mapster` 做 DTO、LineModel、Request、Response 映射。
- 继续复用或迁移 MateWpfThemes 中有价值的控件、主题和 DataGrid 交互能力。

.NET 10 前端不要继续引入：

- `GalaSoft.MvvmLight`。
- MVVMLight `ViewModelBase`。
- MVVMLight `RelayCommand`。
- MVVMLight `Messenger`。

旧 .NET Framework 4.8 前端按 [旧 WPF/VSTO 页面总纲](前端页面总纲.md) 和 [WPF MVVMLight Guide](../guides/frontend-wpf/wpf-mvvmlight.md) 维护；只有明确维护旧项目时才读取这些文档。

## 2. 调用方向

当前 .NET 10 前端仍然只是界面层，不直接处理数据库和后端业务事务。

```text
View
  -> Binding / Command
ViewModel
  -> Refit API Client / 前端 UI Service
WebApi
  -> Application Service
  -> Repository Interface
  -> Dapper Repository
```

ViewModel 可以包含：

- 页面状态。
- Binding 属性。
- Command。
- 简单 UI 编排。
- 调用 Refit API Client。
- 调用 DialogService、MessageService、NavigationService。
- DTO 与前端 Model / LineModel 的转换入口。

ViewModel 禁止包含：

- SQL / Dapper / Repository 实现。
- 直接 `new Repository`、`new Controller`、`new Window`。
- 后端业务事务控制。
- 复杂业务计算。
- 大量手写 DTO 字段复制。

## 3. MVVM 基类层次

当前 .NET 10 前端建议形成以下基础设施：

```text
ObservableObjectEx
  只提供属性通知基础和 SuppressScope / IsSuppressed 抑制机制。

ViewModelBaseEx
  页面级状态、Busy、只读保护、异常包装、命令刷新辅助。

BaseLineModel<TDto>
  DataGrid 单行状态、DTO 转换、验证、脏数据、删除状态、提交和回滚。

BaseListModel<TLine,TDto>
  DataGrid 集合、SelectedItem、删除池、批量勾选、变更集、列表级验证。

GridChangeSet<TDto>
  UI 层通用变更集，表达 Added、Modified、DeletedIds。
```

核心原则：

- `BaseLineModel<TDto>` 不继承 `ViewModelBaseEx`。
- 行模型不拥有 Busy、导航、弹窗、消息提示等页面级能力。
- 页面状态和行状态分离。
- `BaseListModel<TLine,TDto>` 不直接依赖后端请求 DTO；由 ViewModel 适配为具体 API Request。

## 4. ViewModelBaseEx 目标职责

`ViewModelBaseEx` 只承载页面通用能力：

- `Title`
- `BusyMessage`
- `ValidationSummary`
- `IsDirty`
- `IsBusy`
- `IsNotBusy`
- `IsReadOnly`
- `CanWrite`
- `RunBusyAsync`
- `RunWriteAsync`
- 命令状态刷新辅助
- 异常处理入口
- 只读违规处理入口

不放入：

- 具体页面保存逻辑。
- 具体页面查询逻辑。
- 产品、BOM、销售订单、采购等业务规则。
- SQL、Repository、WebApi Controller。
- DataGrid 行字段。

`RunBusyAsync` 负责 Busy 计数、异常包装和重复点击保护。`RunWriteAsync` 在 `IsReadOnly == true` 时拒绝写操作。

## 5. CommunityToolkit 写法

页面 ViewModel 使用：

- `[ObservableProperty]` 定义绑定属性。
- `[RelayCommand]` 定义命令。
- `[NotifyCanExecuteChangedFor]` 处理属性变化后的命令刷新。

异步命令使用 `Task`，不要把业务命令写成 `async void`。

```csharp
[RelayCommand(CanExecute = nameof(CanSave))]
private async Task SaveAsync()
{
    await RunWriteAsync(async () =>
    {
        var error = await Lines.ValidateAllAsync();
        if (!string.IsNullOrWhiteSpace(error))
        {
            ValidationSummary = error;
            return;
        }

        var changeSet = Lines.GetChangeSet();
        await _api.SaveAsync(changeSet.ToRequest());

        Lines.AcceptChanges();
        AcceptChanges();
        _messageService.ShowSuccess("保存成功。");
    }, "正在保存...");
}
```

构造函数只做：

- 保存注入对象。
- 初始化简单默认值。
- 初始化行集合。
- 注册列表状态变化事件。

不要在构造函数中执行大量异步加载。

## 6. DataGrid 行模型与集合模型

可编辑 DataGrid 不直接绑定 DTO。

推荐流程：

```text
Contracts DTO
  -> LineModel
  -> BaseListModel
  -> DataGrid
  -> GetChangeSet()
  -> Request DTO
  -> Refit API
```

`BaseLineModel<TDto>` 目标能力：

- `Uid`
- `Version`
- `State`
- `IsDirty`
- `IsNew`
- `IsModified`
- `IsDeleted`
- `IsChecked`
- `IsSelected`
- `IsReadOnly`
- `RowIndex`
- `LoadFromDto`
- `ToDto`
- `MarkAsAdded`
- `MarkAsModified`
- `MarkAsDeleted`
- `RestoreFromDeleted`
- `AcceptChanges`
- `RejectChanges`
- 字段级、行级和异步验证

`BaseListModel<TLine,TDto>` 目标能力：

- `Items`
- 内部完整集合，保留已删除持久化行。
- `SelectedItem`
- `HasChanges`
- `IsAllValid`
- `IsAllChecked`
- `CheckedCount`
- `AddNew`
- `Remove`
- `RemoveCompletely`
- `LoadFromDtos`
- `GetChangeSet`
- `AcceptChanges`
- `RejectChanges`
- `ValidateAllAsync`
- `Reindex`

删除规则：

- 删除已持久化行：从 `Items` 移除，在内部集合保留，状态设为 `Deleted`，保存时进入 `DeletedIds`。
- 删除新增行：直接从 UI 集合和内部集合移除，不进入删除集。
- 保存成功后：`Lines.AcceptChanges()`。
- 取消编辑时：`Lines.RejectChanges()`。

## 7. UI Service 边界

CommunityToolkit 不内置 Dialog、Message、Navigation，需要 SwiftExport 自己定义前端 UI Service。

建议服务：

- `INavigationService`：只负责页面路由和当前 ViewModel，不保存数据，不做业务判断。
- `IDialogService`：负责确认框、错误详情、文件选择、编辑弹窗等阻塞交互。
- `IMessageService`：负责保存成功、状态栏、Toast、非阻塞提示。
- `IStatusMessageService`：给 Shell / MainWindow 绑定当前状态消息。

使用原则：

- ViewModel 不直接 `MessageBox.Show`。
- ViewModel 不直接 `new Window()` 或 `new Page()`。
- 保存成功优先走 `IMessageService`，不要弹阻塞窗口。
- 删除确认、放弃未保存变更、危险动作确认走 `IDialogService`。
- 页面导航前的未保存变更判断由当前页面 ViewModel 或 Shell 协调，不塞进 NavigationService。

## 8. Navigation 目标模式

推荐模式：

```text
ShellViewModel.Navigation.CurrentViewModel
  -> ContentControl.Content
  -> DataTemplate 根据 ViewModel 类型匹配 View
```

路由服务只持有：

- `CurrentViewModel`
- `CurrentRoute`
- route 到 ViewModel 类型的注册表
- `NavigateTo(route)`
- `CanNavigateTo(route)`

不要让 NavigationService 承担：

- 保存数据。
- 弹确认框。
- 权限判断。
- SQL 或 API 调用。
- 业务流程状态判断。

## 9. Dialog / Message 目标模式

`IDialogService` 用于阻塞式交互：

- 删除确认。
- 放弃未保存变更确认。
- 错误详情。
- 文件选择。
- 需要独立结果的编辑弹窗。

`IMessageService` 用于非阻塞提示：

- 保存成功。
- 状态栏提示。
- 普通警告。
- 非阻塞错误。

如果 Shell 需要统一显示消息，`IMessageService` 和 `IStatusMessageService` 应指向同一个 `MessageService` 实例。

## 10. 分页控件规则

分页控件优先采用“命令驱动 + 单向绑定”：

- `PageIndex`、`PageSize` 从 ViewModel 单向绑定到控件。
- 用户点击页码或修改每页条数时，控件发送 `PageChangeRequest` 给 ViewModel。
- ViewModel 先检查未保存变更。
- 用户取消翻页时，ViewModel 不修改 `PageIndex/PageSize`，控件保持旧状态。
- 用户确认翻页时，ViewModel 修改分页状态并重新查询。

分页控件不要绕过 ViewModel 自行改业务查询状态。

## 11. 图片列规则

DataGrid 图片列应关注性能和文件锁：

- 图片转换器使用缩略图解码，不直接加载原图尺寸。
- 使用 `BitmapCacheOption.OnLoad` 释放文件锁。
- `BitmapImage.Freeze()` 提升跨线程和渲染稳定性。
- 可以使用 LRU 缓存限制图片数量。
- 图片预览可以使用 ToolTip 或独立预览弹窗。

图片路径转换器只做展示转换，不做业务判断。

## 12. Mapster 规则

当前 .NET 10 前端继续使用 Mapster：

- DTO 到 LineModel：允许 `dto.Adapt(this)`。
- LineModel 到 DTO：允许 `this.Adapt<TDto>()`。
- Request / Response / DTO 转换使用 Mapster，避免手写几十个字段。
- Dapper 直接返回 DTO 时，不需要再 Mapster。
- 复杂映射放在明确 Mapping 配置中。
- 前端 DTO 与 UI Model / LineModel 映射放在前端项目 Mapping 区域。
- 不在 ViewModel 中注册 Mapster 全局配置。
- 不在 Repository 中配置 Mapster。

受控字段如 `Uid`、`Version`、`CreatedBy`、`CreatedDate`、`UpdatedBy`、`UpdatedDate` 必须按业务语义显式处理，不要无脑双向覆盖。

## 13. Features 目录组织规则

`SwiftExport.Client.Wpf/Features/` 是新客户端具体业务视图的聚集地，目录按“业务域 / 功能单元 / 文件角色”组织，不按全项目 `Views`、`ViewModels`、`Models` 大桶组织。

推荐结构：

```text
Features/
  Products/
    ProductList/
      Views/
      ViewModels/
      Models/
      Services/
      Components/
    Categories/
      Views/
      ViewModels/
      Models/
      Services/
    Bom/
    CustomerProducts/
    SupplierProducts/
    Placeholders/
  Sales/
    Orders/
    Customers/
    Workbench/
  Planning/
  Purchasing/
  Shipping/
```

规则：

- `Features/{业务域}/{功能单元}/...` 是业务页面的基本组织单位。
- `Views` 放 XAML 和 code-behind。
- `ViewModels` 放页面 ViewModel、Dialog ViewModel 和有独立业务语义的子 ViewModel。
- `Models` 放 `LineModel`、`TreeItemModel`、`EditModel`、`SearchModel`、`StateModel` 等前端模型。
- `Services` 放只服务当前功能单元的 Dialog Service、Loader、Adapter、Cache 等。
- `Components` 只放有独立业务语义并值得复用的子 View / 子 ViewModel，不为机械拆分 XAML 而新增。
- 全局通用控件继续放 `Resources/Controls`；全局 MVVM 基类继续放 `Mvvm`；Shell、Navigation、UI Service 不放进 `Features`。
- 新页面不要继续把多个业务功能文件堆在业务域根目录，例如不要把产品资料、产品分类、BOM、客户产品全部放在 `Features/Products` 根层。

## 14. 旧前端兼容边界

- 不在旧 MVVMLight 项目中局部引入 `[ObservableProperty]` 作为默认写法。
- 新旧前端可以并存，但页面、基类、DI 和路由不要互相混用。
- 维护旧 WPF/VSTO 时读取 [旧 WPF/VSTO 页面总纲](前端页面总纲.md)，不要把旧规则带入当前 .NET 10 前端。

## 15. 后续待设计

- MateWpfThemes 迁移到 .NET 10 的方式。
- VSTO 是否继续承载 .NET 10 WPF，或保留旧宿主只调用 WebApi。
- 新旧前端并行期间的 Refit BaseUrl、登录态和配置策略。
