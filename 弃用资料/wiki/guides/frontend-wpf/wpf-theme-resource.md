# WPF Theme Resource Guide

## 适用场景

用于调整 WPF 主题资源、样式、资源字典、控件视觉和 MateWpfThemes.FW 兼容问题。

## SwiftExport 规则

- 优先复用 `MateWpfThemes.FW` 和 `SwiftExport.View.FW/Resources` 中已有样式。
- 修改样式前先确认作用域：全局资源、模块资源、单个 View。
- 单页面视觉修复优先局部化，避免无意影响全局控件。
- 新增资源命名要表达用途，避免颜色、尺寸、控件状态混在一起。
- 对 DataGrid、Dialog、ComboBox、TextBox 等共享控件改动要扩大检查范围。
- 新增搜索选择场景优先使用 `SearchableTextBoxPlus`，它用于搜索输入和选中显示分离的场景。
- 旧 `SearchableTextBox` 只作为既有页面兼容控件，不作为新功能首选。

## 修改前检查

- 目标样式来自哪个资源字典。
- 是否影响 MateWpfThemes.FW 共享控件。
- 是否有设计时资源、运行时资源、VSTO 宿主差异。
- 是否需要在 DevApp 和 VSTO 宿主都验证。
- 如果替换搜索选择控件，是否检查了 `Text`、`SelectedItem`、`SelectedValue` 的绑定语义。

## 禁止事项

- 禁止为单个页面修改全局主题而不评估影响。
- 禁止复制大段重复样式形成分叉。
- 禁止在业务 ViewModel 中处理视觉样式逻辑。
- 禁止修改主题资源时顺手改业务绑定。
