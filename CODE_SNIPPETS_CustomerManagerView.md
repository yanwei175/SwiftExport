# 🔧 CustomerManagerView 关键代码片段参考

## 1️⃣ 添加的 DataGrid 列 (XAML)

### 贸易条款字段的列定义

```xaml
<!-- 贸易条款组 - 新增 9 列 -->
<DataGridTextColumn Header="贸易条款" 
                    Binding="{Binding Incoterm}" 
                    Width="90"/>

<DataGridTextColumn Header="贸易地点" 
                    Binding="{Binding IncotermLocation}" 
                    Width="100"/>

<DataGridTextColumn Header="定金比例" 
                    Binding="{Binding DepositRate}" 
                    Width="80"/>

<DataGridTextColumn Header="交货周期(天)" 
                    Binding="{Binding LeadTimeDays}" 
                    Width="100"/>

<DataGridTextColumn Header="付款方式" 
                    Binding="{Binding PaymentMethod}" 
                    Width="90"/>

<DataGridTextColumn Header="币种" 
                    Binding="{Binding Currency}" 
                    Width="70"/>

<DataGridTextColumn Header="承运方式" 
                    Binding="{Binding ShippedBy}" 
                    Width="90"/>

<DataGridTextColumn Header="交期备注" 
                    Binding="{Binding EtdSortRemark}" 
                    Width="120"
                    ElementStyle="{DynamicResource MultiLineDataGridTextBlockStyle}" 
                    EditingElementStyle="{DynamicResource MultiLineDataGridTextBoxStyle}"/>

<DataGridTextColumn Header="贸易条款备注" 
                    Binding="{Binding IncotermSortRemark}" 
                    Width="150"
                    ElementStyle="{DynamicResource MultiLineDataGridTextBlockStyle}" 
                    EditingElementStyle="{DynamicResource MultiLineDataGridTextBoxStyle}"/>
```

### 分页控件修正

```xaml
<!-- ❌ 错误示范 - 客户产品表错误地使用客户表的分页属性 -->
<mt:PaginationControl Grid.Row="2" Margin="0,8,0,0" HorizontalAlignment="Right" 
                      TotalCount="{Binding TotalCustomerTableCount}"     <!-- 错误 -->
                      PageIndex="{Binding PageCustomerTableIndex}"       <!-- 错误 -->
                      PageSize="{Binding PageCustomerTableSize}"/>       <!-- 错误 -->

<!-- ✅ 正确示范 - 使用产品表自己的分页属性 -->
<mt:PaginationControl Grid.Row="2" Margin="0,8,0,0" HorizontalAlignment="Right" 
                      TotalCount="{Binding TotalProductTableCount}" 
                      PageIndex="{Binding PageProductTableIndex, Mode=TwoWay,UpdateSourceTrigger=PropertyChanged}"
                      PageSize="{Binding PageProductTableSize, Mode=TwoWay,UpdateSourceTrigger=PropertyChanged}"/>
```

### 按钮 IsEnabled 绑定

```xaml
<!-- 产品管理工具栏中的按钮 - 添加 IsEnabled 绑定 -->
<Button Margin="0,0,10,0"  
        Style="{StaticResource ButtonPrimary}"
        Content="新增"
        mt:ButtonAssist.IconSize="12"
        mt:ButtonAssist.Icon="{StaticResource Icon.Add}"
        IsEnabled="{Binding HasSelectedCustomer}"  <!-- ← 新增 -->
        Command="{Binding AddProductCommand}" />

<Button Margin="0,0,10,0" 
        Style="{StaticResource ButtonSuccess}"
        Content="保存"
        mt:ButtonAssist.IconSize="12"
        mt:ButtonAssist.Icon="{StaticResource Icon.Save}"
        IsEnabled="{Binding HasSelectedCustomer}"  <!-- ← 新增 -->
        Command="{Binding BatchSaveProductAsyncCommand}" />

<Button Margin="0,0,10,0" 
        Style="{StaticResource ButtonDanger}"
        Content="删除"
        mt:ButtonAssist.IconSize="12"
        mt:ButtonAssist.Icon="{StaticResource Icon.Delete}"
        IsEnabled="{Binding HasSelectedCustomer}"  <!-- ← 新增 -->
        Command="{Binding DeleteProductAsyncCommand}" />

<Button Margin="0,0,10,0"
        Style="{StaticResource ButtonBase}"
        Content="刷新"
        mt:ButtonAssist.IconSize="12"
        mt:ButtonAssist.Icon="{StaticResource Icon.Refresh}"
        IsEnabled="{Binding HasSelectedCustomer}"  <!-- ← 新增 -->
        Command="{Binding CheckAndLoadProductCommand}"/>

<Button Margin="0,0,10,0"
        Style="{StaticResource ButtonBase}"
        Content="反选"
        mt:ButtonAssist.IconSize="12"
        mt:ButtonAssist.Icon="{StaticResource Icon.Refresh}"
        IsEnabled="{Binding HasSelectedCustomer}"  <!-- ← 新增 -->
        Command="{Binding InvertCheckProductCommand}"/>
```

---

## 2️⃣ ViewModel 中的计算属性 (C#)

### HasSelectedCustomer 属性

```csharp
public class CustomerManagerViewModel : ViewModelBaseEx
{
    private CustomerModel _selectedCustomer;

    /// <summary>
    /// 选中的客户（已持久化的有效记录）
    /// 当其值变化时，自动通知 UI 更新 HasSelectedCustomer 绑定
    /// </summary>
    public CustomerModel SelectedCustomer
    {
        get => _selectedCustomer;
        set
        {
            if (SetProperty(ref _selectedCustomer, value))
            {
                // 关键：通知 UI 属性变化，使依赖的按钮状态即时更新
                RaisePropertyChanged(nameof(HasSelectedCustomer));

                if (value == null)
                {
                    return;
                }
            }
        }
    }

    /// <summary>
    /// 是否选择了有效的客户
    /// - true: 已选择一个已保存的客户（Id > 0）
    /// - false: 未选择客户或选择的是新增未保存行（Id = 0）
    /// 
    /// 用途：控制"客户产品管理"标签页的操作按钮可用性
    /// </summary>
    public bool HasSelectedCustomer => _selectedCustomer != null && _selectedCustomer.Id > 0;
}
```

### 实现原理

```csharp
// 当用户在客户表中选择一行时的流程：

// 1. 用户点击客户表中的某一行
//    ↓
// 2. DataGrid 的 SelectedItem 属性绑定变化
//    ↓
// 3. SelectedCustomer.set() 被调用，参数是新选中的 CustomerModel 实例
//    ↓
// 4. SetProperty() 更新 _selectedCustomer 字段
//    ↓
// 5. RaisePropertyChanged(nameof(HasSelectedCustomer)) 通知 UI
//    ↓
// 6. 所有绑定到 HasSelectedCustomer 的 IsEnabled 属性 getter 被调用
//    ↓
// 7. 按钮状态自动更新（启用或禁用）

// 完整的改进前后对比：

// ❌ 改进前 - 用户无法感知状态
public CustomerModel SelectedCustomer
{
    get => _selectedCustomer;
    set => SetProperty(ref _selectedCustomer, value);  // 没有额外处理
}

// ✅ 改进后 - 用户可直观感知状态变化
public CustomerModel SelectedCustomer
{
    get => _selectedCustomer;
    set
    {
        if (SetProperty(ref _selectedCustomer, value))
        {
            RaisePropertyChanged(nameof(HasSelectedCustomer));  // 关键改进
            if (value == null) return;
        }
    }
}
```

---

## 3️⃣ CheckAndLoadProduct 方法 (C#)

### 产品加载的前置校验

```csharp
/// <summary>
/// 检查并加载客户的产品数据
/// 前置条件：
/// 1. 当前 ActiveTab 必须是 CustomerProducts（只在显示时加载，提高性能）
/// 2. 必须选中了一个有效的客户（HasSelectedCustomer = true）
/// </summary>
private void CheckAndLoadProduct()
{
    // 1. 检查是否在正确的标签页
    if (ActiveTab != CustomerManagerTabs.CustomerProducts) 
        return;

    // 2. 检查是否选中了客户
    if (SelectedCustomer == null)
    {
        StatusMessage = "请先选择一个客户以查看其产品信息。";
        return;
    }

    // 3. 校验通过，异步加载产品数据
    _ = LoadCustomerProductAsync();
}

/// <summary>
/// 异步加载客户的产品数据
/// 工作流程：
/// 1. 参数验证
/// 2. 构建查询条件（客户 ID、搜索关键字、分页参数）
/// 3. 调用 API 获取数据
/// 4. 在 UI 线程中更新绑定数据
/// </summary>
private async Task LoadCustomerProductAsync()
{
    if (_customerProductApi == null) return;
    if (SelectedCustomer == null)
    {
        StatusMessage = "请先选择一个客户以查看其产品信息。";
        return;
    }

    var customerId = SelectedCustomer.Id;

    using (BusyScope())  // 加载期间设置 IsBusy = true，按钮自动禁用
    {
        try
        {
            // 构建查询参数
            var requestDto = new ProductSearchDto
            {
                Keyword = SearchProductText,
                CustomerIds = new List<int> { customerId },
                PageIndex = PageProductTableIndex,
                PageSize = PageProductTableSize
            };

            // 调用 API 获取分页数据
            var result = await _customerProductApi.GetCustomerProductPagedListAsync(requestDto);

            // 在 UI 线程中更新数据
            DispatcherProvider.BeginInvoke(() =>
            {
                if (result.Success && result.Data != null)
                {
                    var dtos = result.Data.Items.ToList();

                    // 将 DTO 列表转换为 Model 列表并加载到 CurrentProductTable
                    CurrentProductTable.ReLoadRangeFromDtoList(dtos);

                    // 更新分页信息
                    TotalProductTableCount = result.Data.TotalCount;

                    // 显示成功消息
                    StatusMessage = $"加载成功，共 {TotalProductTableCount} 条。";
                    return;
                }
                else
                {
                    StatusMessage = "加载失败: " + result.Message;
                }
            });
        }
        catch (Exception ex)
        {
            StatusMessage = "加载异常: " + ex.Message;
        }
    }
}
```

### 调用流程

```csharp
// 在 ViewModel 初始化时
public class CustomerManagerViewModel : ViewModelBaseEx
{
    public CustomerManagerViewModel(...)
    {
        // ... 其他初始化代码 ...

        // 当 ActiveTab 属性变化时
        // 如果切换到"客户产品管理"标签（值为 CustomerManagerTabs.CustomerProducts）
        // 则自动触发 CheckAndLoadProduct()
    }
}

// 在 TabControl 的 SelectedIndex 绑定变化时
// → ActiveTab.set() 被调用
// → 检查新值是否为 CustomerManagerTabs.CustomerProducts
// → 是则调用 CheckAndLoadProduct()
// → CheckAndLoadProduct() 验证条件后加载数据

private CustomerManagerTabs _activeTab;
public CustomerManagerTabs ActiveTab
{
    get => _activeTab;
    set
    {
        if (SetProperty(ref _activeTab, value) && value == CustomerManagerTabs.CustomerProducts)
        {
            CheckAndLoadProduct();  // ← 当切换到客户产品标签时自动调用
        }
    }
}
```

---

## 4️⃣ Model 中的贸易条款字段 (C#)

### CustomerModel 中的字段定义

```csharp
public class CustomerModel : BaseLineModel, IBaseModel, IDataErrorInfo
{
    // ================= 贸易条款默认值（销售订单默认值来源）=================

    private string _incotermSortRemark;
    /// <summary>
    /// 贸易条款备注（用于定义完整的交易条件）
    /// 例如：FOB Jiangmen, 30% Deposit, Balance before shipment
    /// </summary>
    public string IncotermSortRemark
    {
        get => _incotermSortRemark;
        set => SetProperty(ref _incotermSortRemark, value);
    }

    private string _incoterm;
    /// <summary>
    /// 国际贸易术语（如 EXW / FOB / CIF / DAP / DDP）
    /// </summary>
    public string Incoterm
    {
        get => _incoterm;
        set => SetProperty(ref _incoterm, value);
    }

    private string _incotermLocation;
    /// <summary>
    /// 贸易条款地点（如 FOB Shenzhen 中的 Shenzhen）
    /// </summary>
    public string IncotermLocation
    {
        get => _incotermLocation;
        set => SetProperty(ref _incotermLocation, value);
    }

    private decimal _depositRate;
    /// <summary>
    /// 定金比例（如 30%）
    /// </summary>
    public decimal DepositRate
    {
        get => _depositRate;
        set => SetProperty(ref _depositRate, value);
    }

    private int _leadTimeDays;
    /// <summary>
    /// 交货周期（天数）
    /// </summary>
    public int LeadTimeDays
    {
        get => _leadTimeDays;
        set => SetProperty(ref _leadTimeDays, value);
    }

    private string _paymentMethod;
    /// <summary>
    /// 付款方式（如 TT / LC / OA）
    /// </summary>
    public string PaymentMethod
    {
        get => _paymentMethod;
        set => SetProperty(ref _paymentMethod, value);
    }

    private string _currency;
    /// <summary>
    /// 币种（USD / RMB / EUR 等）
    /// </summary>
    public string Currency
    {
        get => _currency;
        set => SetProperty(ref _currency, value);
    }

    private string _etdSortRemark;
    /// <summary>
    /// ETD 备注（用于告知客户预计离港时间）
    /// </summary>
    public string EtdSortRemark
    {
        get => _etdSortRemark;
        set => SetProperty(ref _etdSortRemark, value);
    }

    private string _shippedBy;
    /// <summary>
    /// 承运方式（如 DHL / UPS / 海运）
    /// </summary>
    public string ShippedBy
    {
        get => _shippedBy;
        set => SetProperty(ref _shippedBy, value);
    }
}
```

### 字段的特点

```csharp
// ✅ 所有贸易条款字段都使用 SetProperty() 赋值
// 这确保了：
// 1. 自动触发 PropertyChanged 事件（UI 数据绑定）
// 2. 自动触发 Item[] 索引器通知（软验证红框）
// 3. 自动在 ChangeTracker 中记录原始值
// 4. 自动更新 ModelState（Added/Modified/Deleted）

// 在 XAML 中绑定示例：
// <DataGridTextColumn Header="贸易条款" Binding="{Binding Incoterm}" />
// 
// 用户编辑时的工作流：
// 1. 用户在 DataGrid 单元格中输入
// 2. WPF 更新 Model.Incoterm 属性
// 3. SetProperty() 检测到值变化
// 4. 触发 PropertyChanged("Incoterm")
// 5. 触发 PropertyChanged("Item[]")（软验证）
// 6. 触发 PropertyChanged(nameof(IsValid))
// 7. UI 更新绑定、显示红框（如有验证错误）、标记脏数据
```

---

## 5️⃣ 数据映射配置 (Mapster)

### CustomerMapping 的配置

```csharp
public class CustomerMapping : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        // DTO → Model 映射
        config.NewConfig<CustomerDto, CustomerModel>()
            // 基本信息
            .Map(dest => dest.CustomerCode, src => src.CustomerCode)
            .Map(dest => dest.Name, src => src.Name)
            .Map(dest => dest.Address, src => src.Address)
            .Map(dest => dest.PhoneNumber, src => src.PhoneNumber)
            .Map(dest => dest.ContactPerson, src => src.ContactPerson)
            .Map(dest => dest.DocRelateNo, src => src.DocRelateNo)

            // 文件夹路径
            .Map(dest => dest.LabelFolderPath, src => src.LabelFolderPath)
            .Map(dest => dest.ManualFolderPath, src => src.ManualFolderPath)
            .Map(dest => dest.DrawingFolderPath, src => src.DrawingFolderPath)
            .Map(dest => dest.LinedrawingFolderPath, src => src.LinedrawingFolderPath)
            .Map(dest => dest.BarcodeFolderPath, src => src.BarcodeFolderPath)
            .Map(dest => dest.SpecSheetFolderPath, src => src.SpecSheetFolderPath)

            // 贸易条款默认值（新增）
            .Map(dest => dest.IncotermSortRemark, src => src.IncotermSortRemark)
            .Map(dest => dest.Incoterm, src => src.Incoterm)
            .Map(dest => dest.IncotermLocation, src => src.IncotermLocation)
            .Map(dest => dest.DepositRate, src => src.DepositRate)
            .Map(dest => dest.LeadTimeDays, src => src.LeadTimeDays)
            .Map(dest => dest.PaymentMethod, src => src.PaymentMethod)
            .Map(dest => dest.Currency, src => src.Currency)
            .Map(dest => dest.EtdSortRemark, src => src.EtdSortRemark)
            .Map(dest => dest.ShippedBy, src => src.ShippedBy)

            // 基类字段
            .Map(dest => dest.Id, src => src.Id)
            .IgnoreNullValues(true);

        // Model → DTO 映射（逆向）
        config.NewConfig<CustomerModel, CustomerDto>()
            .Map(dest => dest.CustomerCode, src => src.CustomerCode)
            .Map(dest => dest.Name, src => src.Name)
            // ... 其他字段（与 DTO→Model 相同）
            .Map(dest => dest.Id, src => src.Id)
            .IgnoreNullValues(true);
    }
}
```

### 在 LoadFromDto 中的使用

```csharp
public class CustomerModel : BaseLineModel
{
    public override void LoadFromDto<CustomerDto>(CustomerDto dto, bool 抑制清理脏数据 = false)
    {
        if (dto == null) return;

        using (程序内部修改Scope())  // 使用 Scope 防止标记为 Modified
        {
            // 使用 Mapster 映射 DTO 到 Model
            // 所有属性（包括贸易条款字段）都会被自动映射
            dto.Adapt(this);

            if (this.Id == 0)
            {
                this.MarkAsAdded();
            }

            // 根据调用方需要决定是否立即提交/清理脏数据
            if (!抑制清理脏数据)
                CommitChanges();
        }
    }
}

// 调用示例：
// 1. 从 API 获取 CustomerDto 列表
// var dtos = await _customerApi.GetPagedAsync(criteria);
//
// 2. 为每个 DTO 创建对应的 Model 并加载数据
// foreach (var dto in dtos)
// {
//     var model = new CustomerModel();
//     model.LoadFromDto(dto);  // 自动映射包括贸易条款字段
//     _customerList.Add(model);
// }
```

---

## 6️⃣ 按钮命令实现 (C#)

### AddProductCommand 的改进版本

```csharp
public class CustomerManagerViewModel : ViewModelBaseEx
{
    public RelayCommand AddProductCommand { get; }

    public CustomerManagerViewModel(...)
    {
        // 初始化命令
        AddProductCommand = new RelayCommand(AddProduct);
        // ...
    }

    private void AddProduct()
    {
        // ✅ 改进：在命令执行时二次校验，保证安全
        if (SelectedCustomer == null)
        {
            StatusMessage = "未选择客户";
            return;
        }

        var model = new CustomerProductModel()
        {
            CustomerId = SelectedCustomer.Id,
            CustomerSku = "新客户编码",
            Sku = "aksldjlak"
        };
        model.MarkAsAdded();
        CurrentProductTable.AddModelToFirst(model);
        StatusMessage = "已添加新产品行，请编辑相关信息并保存。";
    }
}

// ❌ 改进前的问题：
// - 即使 HasSelectedCustomer = false，用户也可能通过其他方式触发命令
// - 没有二次校验，可能导致异常

// ✅ 改进后的好处：
// 1. 按钮禁用提前提示（一级防护）
// 2. 命令执行时二次校验（二级防护）
// 3. 用户体验更友好
```

---

## 7️⃣ 测试代码片段

### 单元测试示例

```csharp
[TestClass]
public class CustomerManagerViewModelTests
{
    [TestMethod]
    public void HasSelectedCustomer_WhenNullCustomer_ShouldBeFalse()
    {
        // Arrange
        var vm = new CustomerManagerViewModel(...);
        vm.SelectedCustomer = null;

        // Act
        var result = vm.HasSelectedCustomer;

        // Assert
        Assert.IsFalse(result);
    }

    [TestMethod]
    public void HasSelectedCustomer_WhenNewUnsavedCustomer_ShouldBeFalse()
    {
        // Arrange
        var vm = new CustomerManagerViewModel(...);
        var customer = new CustomerModel { Id = 0 };  // 新增未保存
        vm.SelectedCustomer = customer;

        // Act
        var result = vm.HasSelectedCustomer;

        // Assert
        Assert.IsFalse(result);
    }

    [TestMethod]
    public void HasSelectedCustomer_WhenSavedCustomer_ShouldBeTrue()
    {
        // Arrange
        var vm = new CustomerManagerViewModel(...);
        var customer = new CustomerModel { Id = 1 };  // 已保存
        vm.SelectedCustomer = customer;

        // Act
        var result = vm.HasSelectedCustomer;

        // Assert
        Assert.IsTrue(result);
    }

    [TestMethod]
    public void SelectedCustomer_WhenChanged_ShouldRaisePropertyChanged()
    {
        // Arrange
        var vm = new CustomerManagerViewModel(...);
        var propertyChangedRaised = false;
        vm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(vm.HasSelectedCustomer))
                propertyChangedRaised = true;
        };

        // Act
        vm.SelectedCustomer = new CustomerModel { Id = 1 };

        // Assert
        Assert.IsTrue(propertyChangedRaised);
    }
}
```

---

## 8️⃣ 调试技巧

### 在 ViewModel 中添加调试输出

```csharp
public class CustomerManagerViewModel : ViewModelBaseEx
{
    public bool HasSelectedCustomer
    {
        get
        {
            var result = _selectedCustomer != null && _selectedCustomer.Id > 0;
            Debug.WriteLine($"[DEBUG] HasSelectedCustomer: {result}");
            Debug.WriteLine($"  SelectedCustomer: {_selectedCustomer?.CustomerCode ?? "null"}");
            Debug.WriteLine($"  Id: {_selectedCustomer?.Id}");
            return result;
        }
    }

    public CustomerModel SelectedCustomer
    {
        get => _selectedCustomer;
        set
        {
            Debug.WriteLine($"[DEBUG] SelectedCustomer setter called");
            Debug.WriteLine($"  Old Value: {_selectedCustomer?.CustomerCode ?? "null"}");
            Debug.WriteLine($"  New Value: {value?.CustomerCode ?? "null"}");

            if (SetProperty(ref _selectedCustomer, value))
            {
                Debug.WriteLine($"[DEBUG] RaisePropertyChanged(nameof(HasSelectedCustomer))");
                RaisePropertyChanged(nameof(HasSelectedCustomer));

                if (value == null)
                {
                    Debug.WriteLine($"[DEBUG] Selected customer is null, returning");
                    return;
                }
            }
            else
            {
                Debug.WriteLine($"[DEBUG] SetProperty returned false, value not changed");
            }
        }
    }
}
```

### 在 XAML 中添加调试绑定

```xaml
<!-- 在按钮旁添加文本块显示状态 -->
<StackPanel Orientation="Vertical">
    <Button IsEnabled="{Binding HasSelectedCustomer}" 
            Content="新增"
            Command="{Binding AddProductCommand}" />

    <!-- 调试用 - 显示当前状态 -->
    <TextBlock Foreground="Red"
               Text="{Binding HasSelectedCustomer, StringFormat='HasSelectedCustomer: {0}'}"/>
    <TextBlock Foreground="Red"
               Text="{Binding SelectedCustomer.CustomerCode, StringFormat='Selected: {0}'}"/>
</StackPanel>
```

---

## 9️⃣ 常见错误及修复

### 错误 1: 按钮始终禁用

```csharp
// ❌ 错误原因 1: SelectedCustomer 始终为 null
if (SelectedCustomer == null)  // ← 检查这里
    // ...

// ❌ 错误原因 2: 选中的客户 Id 为 0（新增未保存）
if (_selectedCustomer.Id <= 0)  // ← 检查这里
    // ...

// ✅ 修复: 在客户表中选择一个已保存的客户（Id > 0）
var savedCustomer = _customerList.FirstOrDefault(x => x.Id > 0);
SelectedCustomer = savedCustomer;
```

### 错误 2: 产品表分页混乱

```csharp
// ❌ 错误: 使用了客户表的分页属性
TotalCount="{Binding TotalCustomerTableCount}"
PageIndex="{Binding PageCustomerTableIndex}"

// ✅ 修复: 使用产品表的分页属性
TotalCount="{Binding TotalProductTableCount}"
PageIndex="{Binding PageProductTableIndex}"
```

### 错误 3: 贸易条款字段映射失败

```csharp
// ❌ 错误: Mapping 中的字段名不匹配
.Map(dest => dest.Incoterm, src => src.INCOTERM)  // ← 大小写不匹配

// ✅ 修复: 确保字段名完全匹配
.Map(dest => dest.Incoterm, src => src.Incoterm)
```

---

## 🔟 性能优化建议

### 虚拟化大数据集

```xaml
<!-- 当 DataGrid 行数 > 500 时考虑虚拟化 -->
<DataGrid EnableRowVirtualization="True"
          EnableColumnVirtualization="True"
          VirtualizingStackPanel.VirtualizationMode="Recycling">
    <!-- ... -->
</DataGrid>
```

### 异步加载优化

```csharp
// 使用 Task 而不是 blocking 操作
private async void AddProduct()
{
    // ✅ 使用 async/await
    var validated = await _productService.ValidateAsync(productId);

    // ❌ 避免 blocking
    // var validated = _productService.Validate(productId);  // 会阻塞 UI 线程
}
```

### 减少频繁刷新

```csharp
// 在批量操作时使用事件抑制
using (BusyScope())  // 设置 IsBusy = true，防止用户交互
{
    // 批量操作代码
    for (int i = 0; i < 1000; i++)
    {
        // 每次操作都会触发 PropertyChanged，但 UI 在 BusyScope 期间被禁用
        DoOperation(i);
    }
}
// 操作结束后，UI 一次性刷新
```

---

**以上代码片段覆盖了 CustomerManagerView 改进的所有关键实现细节。**
**可复制使用或作为参考进行修改。**

