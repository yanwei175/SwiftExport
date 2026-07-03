# DataGrid 通用编辑控件体系

本体系把 WPF DataGrid 的编辑体验做成类似 AG Grid 的 ColumnDef / Cell Editor：

- `EditableDataGrid` 对应 Grid 级编辑行为，负责 Enter、Delete、Paste 等通用键盘交互。
- Lookup 搜索列推荐使用原生 `DataGridTemplateColumn` + `SearchableTextBoxPlus`，保留 DataGrid 原生编辑生命周期。
- `DataGridSearchableColumn` 是实验性自定义列，暂不作为真实业务页首选。
- `LookupCommitContext` 对应 AG Grid 的 `onCellValueChanged` / editor commit 参数。
- `DataGridNumberColumn` 对应通用数值 Cell Editor，只限制输入和格式化，不做业务校验。

## 主题库边界

主题库只负责通用 UI 编辑体验、候选选择、键盘交互、事件和命令通知。业务层负责：

- 根据候选项回填行模型。
- 管理 IsDirty、验证、保存、撤销。
- 决定 Product、Customer、Supplier、Color 等 Lookup 的数据来源。

不要把 `BaseLineModel` 放进主题库。`BaseLineModel` 属于业务前端状态模型，包含脏数据、验证、删除、DTO 加载等业务语义；主题库必须保持和 ERP 业务类型解耦，才能复用于任意 DataGrid。

## 推荐 Lookup 模板列

示例：

```xml
<DataGridTemplateColumn Header="产品型号">
    <DataGridTemplateColumn.CellTemplate>
        <DataTemplate>
            <TextBlock Text="{Binding CompanyModel}" />
        </DataTemplate>
    </DataGridTemplateColumn.CellTemplate>
    <DataGridTemplateColumn.CellEditingTemplate>
        <DataTemplate>
            <mt:SearchableTextBoxPlus
                Text="{Binding ProductSearchText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
                ItemsSource="{Binding DataContext.ProductLookupItems, RelativeSource={RelativeSource AncestorType=DataGrid}}"
                SelectedItem="{Binding SelectedProductLookup, Mode=TwoWay}"
                SelectedValue="{Binding ProductId, Mode=TwoWay}"
                SelectedValuePath="ProductId"
                DisplayMemberPath="CompanyModel"
                FilterMemberPaths="CompanyModel,DescriptionZh,DescriptionEn"
                SelectionCommitted="ProductLookupEditor_SelectionCommitted" />
        </DataTemplate>
    </DataGridTemplateColumn.CellEditingTemplate>
</DataGridTemplateColumn>
```

业务层在 `SelectionCommitted` 中组装 `LookupCommitContext` 并回填行模型：

- `RowItem`：当前 DataGrid 行对象。
- `SelectedItem`：候选项对象。
- `SelectedValue`：按 `SelectedValuePath` 解析出的值。
- `SearchText`：提交时搜索文本。
- `ColumnKey`：用于区分 Product、Customer、Supplier 等列。
- `CommitReason`：`Enter`、`Tab`、`Mouse`、`ExactMatch`、`Programmatic`。

## 实验性 DataGridSearchableColumn

`DataGridSearchableColumn` 保留给后续实验，不建议真实业务页优先使用。自定义 `DataGridColumn` 需要自行处理打字进入编辑、首字符传递、焦点、虚拟化和 Tab/Enter/Esc 生命周期，维护成本高于模板列。

## ItemTemplate

候选项可用 `ItemTemplate` 展示图片、多列文本或状态标签。主题库不要求候选项类型，只按绑定读取属性。

## 键盘规则

- Enter：确认当前候选，`EditableDataGrid` 默认移动到下一单元格。
- Tab：确认当前候选后保留 DataGrid 原生 Tab 导航。
- Esc：关闭候选弹层并取消当前编辑。
- Delete：`EditableDataGrid` 不直接删除集合，只转发当前行给 `DeleteRowCommand`。
- Ctrl+V：有 `PasteRowsCommand` 时转发剪贴板文本，业务层自行解析。

## 数字列

`DataGridNumberColumn` 支持 `decimal?` 常见输入，提供小数位、显示格式、负数开关、焦点全选和 Enter 提交。

```xml
<mt:DataGridNumberColumn
    Header="Qty"
    Binding="{Binding Qty, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
    DecimalPlaces="2"
    FormatString="N2"
    AllowNegative="False" />
```

## 后续扩展

下一阶段可以继续增加：

- `DataGridDateColumn`：日期输入和显示格式。
- `DataGridCommandColumn`：行内删除、复制、查看图片等按钮列。
- Lookup 远程搜索节流、候选加载状态和空结果提示。
