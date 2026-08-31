---
name: wpf-dotnet10-official-theme
description: SwiftExport.Client.Wpf 官方 WPF Fluent 主题、lepoco/WPF-UI 主题资源、ThemeMode、系统强调色、命名 Style 和资源边界规则。
---

# WPF .NET 10 官方主题 Skill

涉及高对比度、系统强调色、焦点/状态非颜色唯一表达或可访问性验收时，读取 `../quality-gates/SKILL.md`。

## 主题原则

- 默认使用微软官方 WPF Fluent 主题。
- 当前项目同时使用 lepoco/WPF-UI 的 `ThemesDictionary` 和 `ControlsDictionary`。
- WPF-UI 是可用的第三方 Fluent 控件库，详见 `../wpfui-controls/SKILL.md`。
- 不从 Git 历史或归档分支恢复 MateWpfThemes 来覆盖新客户端。
- 不写隐式全局 Style 去替换官方默认控件。
- 项目自定义只通过命名 Style、token、业务控件补充。

## 资源组织

建议：

```text
SwiftExport.Client.Wpf/Resources/
  Colors.xaml
  Tokens.xaml
  Button.xaml
  DataGrid.xaml
  Controls/
```

规则：

- 颜色、间距、圆角、边框等放 token。
- Button/DataGrid 等资源使用命名 Style。
- 单页特殊视觉优先局部资源。
- 全局资源修改前必须搜索影响范围。

## 禁止事项

- 禁止引入旧主题键作为新客户端基础规则。
- 禁止隐式覆盖所有 `TextBox`、`Button`、`DataGrid`。
- 禁止把归档的 MateWpfThemes ResourceDictionary 整块迁移进新客户端。
- 禁止为了单页问题修改全局主题。
