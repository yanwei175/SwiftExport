# 缓存 AutoSuggestBox 使用说明

## 适用控件

- `ProductColorAutoSuggestBox`：绑定 `ProductColorDto`，按 `ColorNameZh` 本地筛选。
- `CustomerAutoSuggestBox`：绑定 `CustomerDto`，按 `CustomerCode` 本地筛选。
- `SupplierAutoSuggestBox`：绑定 `SupplierDto`，按 `SupplierCode` 本地筛选。

三个控件读取 `IReferenceDataCacheService` 暴露的稳定只读集合。控件不调用 API、不初始化缓存，也不维护远程搜索命令。页面仍负责在既有初始化流程中调用 `EnsureLoadedAsync()`。

## 对外接口

| 属性或方法 | 用途 |
|---|---|
| `ItemsSource` | 传入对应的完整缓存集合，类型为 `IList` |
| `SelectedColor` / `SelectedCustomer` / `SelectedSupplier` | 双向绑定完整 DTO；外部可直接读取 `Uid` 和 DTO 其他字段 |
| `SearchText` | 用户当前搜索文字；选中对象的显示回填不会写入该属性 |
| `SelectionCommittedCommand` | 鼠标或 Enter 确认后执行；未指定参数时传完整 DTO |
| `SelectionCommittedCommandParameter` | 可选业务上下文，例如当前编辑行 |
| `SelectionClearedCommand` | 用户明确清空已提交选择后执行 |
| `SelectionClearedCommandParameter` | 清空命令的可选业务上下文 |
| `PlaceholderText` | 与单字段搜索规则一致的提示文字 |
| `FontSize` | 同时控制输入、选中显示和候选主要/辅助文字 |
| `IsSuggestionListOpen` | 双向控制或观察候选弹层 |
| `MaxSuggestionListHeight` | 控制候选弹层最大高度 |
| `ClearButtonEnabled` | 控制 WPF-UI 清空按钮 |
| `FocusEditor()` | View 层需要恢复编辑焦点时调用 |

初版不提供 `SelectedId`、`SelectedValue` 或 `SelectedValuePath`。对象身份始终来自强类型 DTO，不能根据显示文字反查 `Uid`。

## 绑定示例

```xml
<productComponents:ProductColorAutoSuggestBox
    ItemsSource="{Binding ReferenceDataCache.ProductColors}"
    SelectedColor="{Binding SelectedColor, Mode=TwoWay}"
    FontSize="13" />

<customerComponents:CustomerAutoSuggestBox
    ItemsSource="{Binding ReferenceDataCache.Customers}"
    SelectedCustomer="{Binding SelectedCustomer, Mode=TwoWay}"
    SelectionCommittedCommand="{Binding ApplyCustomerCommand}"
    SelectionCommittedCommandParameter="{Binding}"
    FontSize="{Binding EditorFontSize}" />

<supplierComponents:SupplierAutoSuggestBox
    ItemsSource="{Binding ReferenceDataCache.Suppliers}"
    SelectedSupplier="{Binding SelectedSupplier, Mode=TwoWay}"
    SelectionCommittedCommand="{Binding ApplySupplierCommand}"
    FontSize="14" />
```

## 交互边界

- 初始化、外部回填和缓存刷新不会执行提交或清空命令，也不会自动打开候选。
- 方向键只移动候选高亮；鼠标释放或 Enter 才确认一次。
- 用户重新输入时保留上次已提交对象；Esc 或失焦恢复已提交显示。
- 用户把输入明确清空时，先把强类型选择设为 `null`，再执行一次清空命令。
- 外部把 `SearchText` 设为空属于程序回填，即使控件有焦点，也不会清除已选对象或执行清空命令。
- 候选项直接引用缓存中的完整 DTO，不创建包装对象；控件只为当前实例生成筛选结果，不会给共享缓存的默认集合视图设置 `Filter`。
- 控件拦截 WPF-UI 的用户输入筛选事件并只执行一次本地筛选，避免 `DisplayMemberPath` 默认筛选与业务筛选叠加。
- `QuerySubmitted` 保留为 WPF-UI 的输入提交事件，不建立第二条筛选或远程查询通道。
