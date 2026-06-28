# WPF MVVMLight Guide

## 适用场景

适用于 .NET Framework WPF 或历史项目中仍使用 MVVMLight 的页面维护和小步重构。

## 推荐做法

- 保持 MVVMLight 项目原有 `ViewModelBase`、`RelayCommand`、消息机制和 DI 风格。
- 新增页面时优先匹配现有基类、命名空间和绑定惯例。
- 如果项目已有扩展基类，应统一继承项目基类，而不是直接混用多个基类。
- DataGrid 仍应使用行模型承载 UI 状态，避免 DTO 直接承担编辑状态。
- 迁移到 CommunityToolkit 前应单独规划，不在普通功能修改中顺手迁移框架。

## 禁止事项

- 禁止在 MVVMLight 项目中局部引入 CommunityToolkit 属性生成器。
- 禁止一次性替换整个 MVVM 框架。
- 禁止改变大量绑定名称来迁就新写法。
- 禁止 ViewModel 直接访问数据库或窗口实例。
- 禁止把旧项目兼容规则上升为 .NET 8 新项目默认规则。

## Codex 修改前检查清单

- 当前项目是否确实是 .NET Framework WPF/MVVMLight。
- 现有基类、命令、消息、IoC 容器和资源字典是否已确认。
- 修改是否会影响 XAML 绑定名称。
- 是否需要保持旧 UI 行为完全不变。
- TODO：待人工确认是否要把 MVVMLight 单独提炼成正式 Skill。

## 常见错误

- 在旧框架项目里复制 .NET 8 CommunityToolkit 模板。
- 绑定属性未触发通知。
- 命令刷新和选择状态不同步。
- 重构时同时改框架、目录、业务逻辑，导致风险过大。
- 忽略旧项目的资源和样式依赖。
