# WPF MVVMLight Guide

## 适用场景

用于维护 SwiftExport 中 .NET Framework 4.8 WPF / MVVMLight 页面、ViewModel、命令和消息机制。

## SwiftExport 规则

- 保持项目现有 `ViewModelBase`、`RelayCommand`、消息机制和 DI 风格。
- 页面 ViewModel 优先继承 `SwiftExport.View.FW.Mvvm.ViewModelBaseEx`，并复用其中 Busy、命令刷新、Messenger、DisabledState 等机制。
- `ViewModelBaseEx` 是页面级 ViewModel 基类；行模型、树节点、只读导航节点不应为了属性通知而继承它。
- 轻量可绑定对象优先继承 `SwiftExport.View.FW.Mvvm.ObservableObjectBase`；该基类只提供属性通知、`SetProperty` 和 `DisabledStateScope`，不包含 Busy、Messenger、Navigation。
- 命令优先使用 `GalaSoft.MvvmLight.Command.RelayCommand` / `RelayCommand<T>`。
- 异步命令用 `async () => await XxxAsync()` 包装；不要把业务命令写成 `async void`。
- 新增页面时匹配已有模块目录、基类、命名空间和绑定惯例。
- 如果项目已有扩展基类，应统一继承项目基类。
- DataGrid 可编辑行使用前端 Model / LineModel 承载 UI 状态，避免 DTO 直接承担编辑状态。
- 迁移到 CommunityToolkit MVVM 必须单独规划，不在普通功能修改中顺手迁移框架。
- 普通任务默认兼容 .NET Framework 4.8，不使用 record、init、required、file-scoped namespace、primary constructor、global using、nullable reference types 强依赖。

## 修改前检查

- 当前页面基类、命令、消息、IoC 容器和资源字典是否已确认。
- 修改是否影响 XAML 绑定名称。
- 是否需要保持旧 UI 行为完全不变。
- 是否已有 `RegisterBusyNotifyCommand`、`IDialogService`、`ViewRouteService` 或模块级 ListModel 可复用。

## 禁止事项

- 禁止在 MVVMLight 项目中局部引入 CommunityToolkit 属性生成器作为默认写法。
- 禁止一次性替换整个 MVVM 框架。
- 禁止 ViewModel 直接访问数据库或窗口实例。
- 禁止重构时同时改框架、目录和业务逻辑。
- 禁止 ViewModel 直接 new Repository、new WebApi Controller、new Window。
- 禁止让非页面 Model 继承 `ViewModelBaseEx` 来获取属性通知；应使用 `ObservableObjectBase` 或现有行模型基类。
