# 缓存 AutoSuggestComboBox 使用说明

## 适用控件

- `ProductColorAutoSuggestComboBox`：绑定 `ProductColorDto`，只按 `ColorNameZh` 本地筛选。
- `CustomerAutoSuggestComboBox`：绑定 `CustomerDto`，只按 `CustomerCode` 本地筛选。
- `SupplierAutoSuggestComboBox`：绑定 `SupplierDto`，只按 `SupplierCode` 本地筛选。

三个控件都是已有 `LookupTextBox` 的薄封装，固定使用 `SearchMode=Local`、`RequireItemMatch=true`、`ClearSelectionOnEdit=false`。它们读取 `IReferenceDataCacheService` 暴露的稳定只读集合，不调用 API、不初始化缓存，也不提供远程模式切换。页面负责在初始化流程中调用 `EnsureLoadedAsync()`。

## 对外接口

| 属性或方法 | 用途 |
|---|---|
| `ItemsSource` | 传入对应的完整缓存集合，类型为 `IEnumerable` |
| `SelectedColor` / `SelectedCustomer` / `SelectedSupplier` | 双向绑定完整 DTO；外部可直接读取 `Uid` 和 DTO 其他字段 |
| `SelectionCommittedCommand` | 鼠标或 Enter 确认后执行；未指定参数时传完整 DTO |
| `SelectionCommittedCommandParameter` | 可选业务上下文，例如当前编辑行 |
| `SelectionClearedCommand` | 用户明确清空已提交选择后执行 |
| `SelectionClearedCommandParameter` | 清空命令的可选业务上下文 |
| `Placeholder` | 与单字段搜索规则一致的提示文字 |
| `FontSize` | 同时控制输入、选中显示和候选主要/辅助文字 |

控件不转发 `LookupTextBox.Text`、远程搜索命令或模式属性。对象身份始终来自强类型 DTO，不能根据显示文字反查 `Uid`。

## 绑定示例

```xml
<productComponents:ProductColorAutoSuggestComboBox
    ItemsSource="{Binding ReferenceDataCache.ProductColors}"
    SelectedColor="{Binding SelectedColor, Mode=TwoWay}"
    FontSize="13" />

<customerComponents:CustomerAutoSuggestComboBox
    ItemsSource="{Binding ReferenceDataCache.Customers}"
    SelectedCustomer="{Binding SelectedCustomer, Mode=TwoWay}"
    SelectionCommittedCommand="{Binding ApplyCustomerCommand}"
    SelectionCommittedCommandParameter="{Binding}"
    FontSize="{Binding EditorFontSize}" />

<supplierComponents:SupplierAutoSuggestComboBox
    ItemsSource="{Binding ReferenceDataCache.Suppliers}"
    SelectedSupplier="{Binding SelectedSupplier, Mode=TwoWay}"
    SelectionCommittedCommand="{Binding ApplySupplierCommand}"
    FontSize="14" />
```

## 交互边界

- 初始化、外部回填和缓存刷新不会执行提交或清空命令，也不会自动打开候选。
- 下拉箭头使用 `LookupTextBox.ShowDropDownButton`；已有选择时展开全部缓存并保留选择，编辑关键词时展示当前过滤结果，再次点击收起。
- 方向键移动高亮；鼠标、Enter 或 Tab 使用 `LookupTextBox` 的既有提交路径。
- 用户重新输入时保留上次已提交对象；Esc 或失焦恢复已提交显示。
- 只有显式点击清空按钮才清除强类型选择并执行一次清空命令。
- 候选项直接引用缓存中的完整 DTO，不创建包装对象；每个 `LookupTextBox` 使用独立的集合视图进行本地过滤。
- `FontSize` 传给输入、选中显示和候选列表；其他旧 `LookupTextBox` 默认仍使用原候选字号。
