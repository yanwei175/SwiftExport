# WPF MVVMLight Guide

## 适用场景

用于维护 SwiftExport 中 .NET Framework 4.8 WPF / MVVMLight 页面、ViewModel、命令和消息机制。

## SwiftExport 规则

- 保持项目现有 `ViewModelBase`、`RelayCommand`、消息机制和 DI 风格。
- 新增页面时匹配已有模块目录、基类、命名空间和绑定惯例。
- 如果项目已有扩展基类，应统一继承项目基类。
- DataGrid 可编辑行使用前端 Model / LineModel 承载 UI 状态，避免 DTO 直接承担编辑状态。
- 迁移到 CommunityToolkit MVVM 必须单独规划，不在普通功能修改中顺手迁移框架。

## 修改前检查

- 当前页面基类、命令、消息、IoC 容器和资源字典是否已确认。
- 修改是否影响 XAML 绑定名称。
- 是否需要保持旧 UI 行为完全不变。

## 禁止事项

- 禁止在 MVVMLight 项目中局部引入 CommunityToolkit 属性生成器作为默认写法。
- 禁止一次性替换整个 MVVM 框架。
- 禁止 ViewModel 直接访问数据库或窗口实例。
- 禁止重构时同时改框架、目录和业务逻辑。

