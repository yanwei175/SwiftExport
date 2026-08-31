---
name: wpf-dotnet10-search-textbox
description: SwiftExport .NET 10 WPF 搜索输入、搜索选择器、远程搜索下拉和可复用搜索控件设计规则。用于新增或重构搜索框、选择框、SearchableTextBoxPlus 类控件、Popup 搜索、DataGrid 内搜索编辑器和关键词查询体验。
---

# SwiftExport 搜索文本框与搜索选择控件 Skill

## 适用范围

用于 `SwiftExport.Client.Wpf` 当前 .NET 10 WPF 前端中的搜索输入和搜索选择控件。

触发场景：

- 新增关键词搜索框。
- 新增远程搜索选择器。
- 新增客户、产品、供应商、仓库、物料等 Lookup 控件。
- 重构旧搜索框、下拉选择、Popup 搜索体验。
- 为 DataGrid 单元格增加搜索选择编辑器。

## 必读上下文

执行前先读：

- `AGENTS.md`
- `.agents/frontend.toml`
- `.agents/workflows/前端NET10工作流.md`
- `.codex/skills/wpf-dotnet10-design/index/SKILL.md`
- `.codex/skills/wpf-dotnet10-design/communitytoolkit-mvvm/SKILL.md`
- `.codex/skills/wpf-dotnet10-design/custom-controls/SKILL.md`
- 涉及键盘、焦点、Automation、高对比度、搜索性能或测试证据时，读取 `.codex/skills/wpf-dotnet10-design/quality-gates/SKILL.md`
- 目标页面、目标 ViewModel、目标控件源码

## 核心原则

- 当前前端不直接复用 Git 历史中旧 MateWpfThemes 的 `SearchableTextBox` 规则。
- 搜索输入文本和已选中对象显示应分离，避免选中项回填输入框后触发重复搜索。
- 简单关键词搜索优先使用普通 `TextBox` + 显式 Search 命令。
- 有对象选择语义时，使用搜索选择器模型：`SearchText`、`SelectedItem`、`SelectedValue`、`ItemsSource`、`IsDropDownOpen`。
- 远程搜索由 ViewModel 或前端服务调用 Refit API；控件只负责输入、弹层、选择和交互状态。
- 远程 SearchCommand 只能由用户正在编辑时的文本变化触发；初始化 Binding、SelectedItem 显示回填、ItemsSource 替换和虚拟化重载不得触发 API 搜索。
- Popup 不得由 Loaded、ItemsSource 初始化或获得焦点自动打开；只允许由用户输入产生的候选结果、显式键盘操作或外部 `IsDropDownOpen` 绑定打开。
- 空文本查看全部候选使用 `Space` 或 `Down`；单击控件只负责获得输入焦点。
- 所有搜索模式在失焦时取消待执行防抖，禁止焦点离开后由计时器重新打开 Popup。
- 控件不得直接调用后端 API、Repository、Application Service。

## 推荐 API

可复用搜索选择控件应优先提供这些能力：

- `SearchText`：当前输入关键词。
- `SelectedItem`：选中的 DTO 或前端模型。
- `SelectedValue`：选中值，通常是 `Uid` 或业务编码。
- `ItemsSource`：候选集合。
- `DisplayMemberPath` 或 `ItemTemplate`：显示候选项。
- `Placeholder`：空值提示。
- `IsDropDownOpen`：弹层状态。
- `SelectionCommittedCommand`：选中后通知页面 ViewModel。
- `MinimumSearchLength`：最小搜索长度。
- `SearchDelay`：输入防抖。
- `IsBusy`：远程搜索等待状态。

## ViewModel 写法

搜索相关属性和命令必须遵循 CommunityToolkit 源生成器：

```csharp
[ObservableProperty]
[NotifyCanExecuteChangedFor(nameof(SearchProductsCommand))]
private string? productKeyword;

[ObservableProperty]
private ProductLookupDto? selectedProduct;

[ObservableProperty]
private ObservableCollection<ProductLookupDto> productCandidates = [];

[RelayCommand(CanExecute = nameof(CanSearchProducts))]
private async Task SearchProductsAsync()
{
    await RunBusyAsync(async () =>
    {
        var result = await _productApi.SearchLookupAsync(ProductKeyword);
        ProductCandidates.Reset(result.Items);
    }, "正在查询产品...");
}

private bool CanSearchProducts()
{
    return IsNotBusy && !string.IsNullOrWhiteSpace(ProductKeyword);
}
```

规则：

- 属性 setter 或 `partial void OnXxxChanged` 中不直接调用 API。
- 需要输入即搜索时，使用防抖服务、控件延迟命令或显式搜索调度，不在 setter 里 `await`。
- 选中对象后，由 `SelectionCommittedCommand` 或绑定的 `SelectedItem` 变化触发页面级回填。
- 页面保存时提交业务 DTO，不提交控件内部状态。

## XAML 规则

- 查询栏关键词搜索可以使用普通 `TextBox`、`Button`、`ProgressRing` 或项目已有控件组合。
- 对象搜索选择优先使用项目封装控件；没有封装时先建立清晰 API，再接入页面。
- 弹层使用 `Popup`、`ListBox`、`DataTemplate` 时要处理键盘、失焦关闭、空结果、加载中和异常提示。
- DataGrid 内搜索编辑器必须保持行高稳定，不因 Popup 或提示文字改变表格布局。

## 禁止事项

- 禁止让控件直接依赖 Refit API、Repository、Controller。
- 禁止把已选对象显示值强行写回 `SearchText` 作为唯一状态。
- 禁止把搜索、保存、过账等业务命令藏在属性 setter 中。
- 禁止把 Git 历史中的旧 WPF/VSTO 或 MateWpfThemes 控件实现原样搬进 `SwiftExport.Client.Wpf`。
- 禁止为了一个页面复制一份不可复用的复杂搜索控件。
