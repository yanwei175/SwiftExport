## CheckBoxDataGridTemplateColumn 使用指南

### 📋 概述
`CheckBoxDataGridTemplateColumn` 是一个可复用的自定义 WPF DataGrid 列类，用于在 DataGrid 中实现虚拟化友好的复选框列，支持全选/反选功能。

### ✨ 特点
- ✅ **无需重复 XAML** - 一行代码替代 20+ 行的模板定义
- ✅ **虚拟化安全** - 基于 DataGridTemplateColumn 的自定义列实现
- ✅ **灵活配置** - 支持自定义属性名称和命令绑定
- ✅ **开箱即用** - 包含合理的默认值

### 🚀 基本用法

#### 在 XAML 中添加命名空间
```xaml
xmlns:mtControls="clr-namespace:MateWpfThemes.FW.Controls;assembly=MateWpfThemes.FW"
```

#### 在 DataGrid 中使用
```xaml
<DataGrid ItemsSource="{Binding Items}">
    <DataGrid.Columns>
        <!-- 自定义复选框列 -->
        <mtControls:CheckBoxDataGridTemplateColumn 
            AllCheckedPropertyName="IsAllChecked"
            ItemCheckedPropertyName="IsChecked"
            ItemCheckedCommandName="ItemCheckedChangedCommand"
            HeaderCheckBoxToolTip="全选/全不选"/>
        
        <!-- 其他列定义 -->
        <DataGridTextColumn Header="名称" Binding="{Binding Name}"/>
    </DataGrid.Columns>
</DataGrid>
```

### 📝 配置属性

| 属性 | 默认值 | 说明 |
|------|------|------|
| `AllCheckedPropertyName` | `IsAllChecked` | DataGrid DataContext 上全选属性的名称 |
| `ItemCheckedPropertyName` | `IsChecked` | 行数据对象上复选框属性的名称 |
| `ItemCheckedCommandName` | `ItemCheckedChangedCommand` | DataGrid DataContext 上的命令名称（行复选框改变时触发） |
| `HeaderCheckBoxToolTip` | `全选/全不选` | 头部复选框的提示文本 |

### 🔧 ViewModel 实现示例

```csharp
public class ProductViewModel : INotifyPropertyChanged
{
    private bool _isAllChecked;
    private ObservableCollection<ProductItem> _items;

    /// <summary>
    /// 全选/全不选标志
    /// </summary>
    public bool IsAllChecked
    {
        get => _isAllChecked;
        set
        {
            if (_isAllChecked != value)
            {
                _isAllChecked = value;
                // 同步所有项的选中状态
                foreach (var item in Items)
                {
                    item.IsChecked = value;
                }
                OnPropertyChanged(nameof(IsAllChecked));
            }
        }
    }

    public ObservableCollection<ProductItem> Items
    {
        get => _items;
        set
        {
            _items = value;
            OnPropertyChanged(nameof(Items));
        }
    }

    /// <summary>
    /// 单项复选框改变时的命令
    /// </summary>
    public ICommand ItemCheckedChangedCommand { get; }

    public ProductViewModel()
    {
        ItemCheckedChangedCommand = new RelayCommand(_ =>
        {
            // 检查是否所有项都被选中
            _isAllChecked = Items.All(x => x.IsChecked);
            OnPropertyChanged(nameof(IsAllChecked));
        });
    }

    public event PropertyChangedEventHandler PropertyChanged;
    
    protected void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// 产品项数据模型
/// </summary>
public class ProductItem
{
    public string Sku { get; set; }
    public string Name { get; set; }

    private bool _isChecked;
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked != value)
            {
                _isChecked = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
            }
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;
}
```

### 📊 与之前方案的对比

| 方面 | 之前（硬编码模板） | 现在（自定义列） |
|------|-----------------|-----------------|
| XAML 代码行数 | 20+ 行 | 5 行 |
| 绑定灵活性 | ❌ 硬编码在模板中 | ✅ 完全可配置 |
| 虚拟化支持 | ✅ 支持 | ✅ 支持 |
| 复用性 | ❌ 需要重复粘贴 | ✅ 一行使用 |
| 维护性 | ❌ 多处修改 | ✅ 一处修改全局有效 |

### 🎯 高级用法

#### 1. 自定义属性名称
如果你的 ViewModel 使用不同的属性名称：

```xaml
<mtControls:CheckBoxDataGridTemplateColumn 
    AllCheckedPropertyName="SelectAll"
    ItemCheckedPropertyName="Selected"
    ItemCheckedCommandName="OnItemSelectedChanged"
    HeaderCheckBoxToolTip="Select/Deselect All"/>
```

#### 2. 自定义提示文本
```xaml
<mtControls:CheckBoxDataGridTemplateColumn 
    HeaderCheckBoxToolTip="Click to select/deselect all items"/>
```

### ⚙️ 实现细节

- **绑定源查找**：
  - 头部复选框通过 `FindAncestor` 查找最近的 DataGrid，然后绑定其 DataContext 的属性
  - 行复选框绑定到行的数据对象
  - 命令通过 `FindAncestor` 查找 UserControl 的 DataContext

- **性能优化**：
  - 使用 `FrameworkElementFactory` 动态生成模板
  - 模板自动 Seal，防止后续修改
  - 支持 DataGrid 虚拟化

### 🔗 相关文件
- 实现：`..\MateWpfThemes\MateWpfThemes.FW\Controls\CheckBoxDataGridTemplateColumn.cs`
- 使用示例：`SwiftExport.View.FW\ProductViews\Views\ProductManagerView.xaml`
- 基础样式：`..\MateWpfThemes\MateWpfThemes.FW\Styles\Base\Controls\DataGrid.xaml`

### 📌 中文注释约定
所有公共类和属性都包含中文注释，提高代码可读性和维护性。

---
**最后更新**：遵循项目指南要求，代码包含完整的中文注释和文档。
