# 行内产品选择器 DataGrid 使用示例

## 前提条件

1. 父 ViewModel 持有 `ObservableCollection<OrderLineViewModel<SaleProductDto>> OrderLines`
2. `OrderLineViewModel<SaleProductDto>` 在构造时注入了：
   - `SearchFunc`：`keyword => _saleApi.SearchProducts(keyword)`
   - `MapToSelectorItem`：`dto => new SelectorItem { DisplayText = dto.Sku, SubText = dto.Name, ImagePath = dto.Image, RawData = dto }`
   - `MapToDto`：`item => (SaleProductDto)item.RawData`

## XAML 命名空间声明

```xml
xmlns:controls="clr-namespace:SwiftExport.View.FW.ProductViews.Controls"
xmlns:assists="clr-namespace:SwiftExport.View.FW.ProductViews.Assists"
xmlns:mt="https://MateWpfTheme.com/wpf/framework/themes"
xmlns:mtPopup="clr-namespace:MateWpfThemes.FW.AttachedProperties;assembly=MateWpfThemes.FW"
```

## DataGrid 行内 Popup 示例

```xml
<DataGrid ItemsSource="{Binding OrderLines}"
          AutoGenerateColumns="False"
          EnableRowVirtualization="True"
          CanUserAddRows="False" IsReadOnly="True"
          RowHeight="38">
    <DataGrid.Columns>

        <!-- 行号列 -->
        <DataGridTextColumn Header="#" Binding="{Binding DisplayText}" Width="60" IsReadOnly="True"/>

        <!-- 产品选择列：按钮 + PopupEx -->
        <DataGridTemplateColumn Header="选择产品" Width="200">
            <DataGridTemplateColumn.CellTemplate>
                <DataTemplate>
                    <Grid>
                        <!-- 触发按钮：显示已选产品或提示文字 -->
                        <Button Content="{Binding DisplayText, TargetNullValue='点击选择...'}"
                                HorizontalContentAlignment="Left" Padding="8,4"
                                Background="Transparent" BorderThickness="0" Cursor="Hand"
                                Command="{Binding OpenSelectorCommand}"/>

                        <!-- PopupEx：跟随 DataGridCell，窗口移动时自动刷新位置 -->
                        <mtPopup:PopupEx IsOpen="{Binding IsSelectorOpen, Mode=TwoWay}"
                                         StaysOpen="False"
                                         AllowsTransparency="True"
                                         Placement="Bottom"
                                         assists:PopupPlacementService.Attach="True">
                            <mtPopup:PopupEx.Effect>
                                <DropShadowEffect Color="Black" Opacity="0.15" BlurRadius="16" ShadowDepth="4"/>
                            </mtPopup:PopupEx.Effect>

                            <!-- 复用行内选择器控件 -->
                            <controls:ProductInlineSelectorControl
                                SearchCommand="{Binding SearchProductsCommand}"
                                ItemsSource="{Binding SearchResults}"
                                SelectedItem="{Binding CurrentSelectorItem, Mode=TwoWay}"
                                ConfirmCommand="{Binding ConfirmProductCommand}"/>
                        </mtPopup:PopupEx>
                    </Grid>
                </DataTemplate>
            </DataGridTemplateColumn.CellTemplate>
        </DataGridTemplateColumn>

        <!-- 其他业务列 -->
        <DataGridTextColumn Header="数量" Width="80"/>
        <DataGridTextColumn Header="单价" Width="100"/>

    </DataGrid.Columns>
</DataGrid>
```

## 多选窗口使用示例（在任意 ViewModel 中调用）

```csharp
// 注入服务
private readonly IProductSelectorService _selectorService;

// 调用示例（BOM 场景，ProductDto 直接透传）
private async void OpenBomProductSelector()
{
    var currentBomItems = _bomItems.Select(x => x.ProductDto).ToList();

    var selected = await _selectorService.ShowMultiSelectorAsync(
        preSelected:     currentBomItems,
        mapToApiDto:     dto => dto,                    // BOM 直接用 ProductDto
        mapFromApiDto:   dto => dto,                    // 直接返回
        buildSearchArgs: null                           // 使用默认搜索参数
    );

    if (selected != null)
    {
        foreach (var product in selected)
            AddBomItem(product);
    }
}

// 调用示例（采购场景，自定义 DTO 双向映射）
private async void OpenPurchaseProductSelector()
{
    var selected = await _selectorService.ShowMultiSelectorAsync<PurchaseItemDto>(
        preSelected:     _existingItems,
        mapToApiDto:     item => new ProductDto { Sku = item.ProductSku, Description = item.ProductName },
        mapFromApiDto:   dto  => new PurchaseItemDto { ProductSku = dto.Sku, ProductName = dto.Description },
        buildSearchArgs: keyword => new ProductSearchDto { Keyword = keyword, CustomerCode = _currentCustomer }
    );
}
```

## 行级别 ViewModel 构造示例

```csharp
// 销售页创建行 VM
var lineVm = new OrderLineViewModel<SaleProductDto>(
    searchFunc:  async keyword => {
        var result = await _salesApi.SearchProducts(new SaleSearchDto { Keyword = keyword, PageSize = 20 });
        return result.Data?.Items ?? Enumerable.Empty<SaleProductDto>();
    },
    mapToItem:   dto => new SelectorItem {
        DisplayText = dto.Sku,
        SubText     = dto.Description,
        ImagePath   = dto.MainImage,
        RawData     = dto
    },
    mapToDto:    item => (SaleProductDto)item.RawData
);

// BOM 页创建行 VM
var bomLineVm = new OrderLineViewModel<ProductDto>(
    searchFunc:  async keyword => {
        var result = await _productApi.GetProductPagedListAsync(
            new ProductSearchDto { Keyword = keyword, PageIndex = 1, PageSize = 20 });
        return result.Success ? result.Data.Items : Enumerable.Empty<ProductDto>();
    },
    mapToItem:   dto => new SelectorItem {
        DisplayText = dto.Sku,
        SubText     = $"{dto.Description} | {dto.Finish}",
        ImagePath   = dto.MainImage,
        RawData     = dto
    },
    mapToDto:    item => (ProductDto)item.RawData
);
```
