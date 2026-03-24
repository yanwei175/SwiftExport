using SwiftExport.View.FW.Mvvm;
using SwiftExport.View.FW.ProductViews.Models;
using System.Collections.ObjectModel;

namespace SwiftExport.View.FW.ProductViews.ViewModels
{
    public class ProductManagerViewModel : ViewModelBase
    {
        // 1. 主表的数据源
        public ObservableCollection<ProductModel> ProductList { get; set; } = new ObservableCollection<ProductModel>();

        // 2. 从表的数据源
        public ObservableCollection<BomItemModel> CurrentBomList { get; set; } = new ObservableCollection<BomItemModel>();

        // 3. ★ 联动的核心：选中的主表行
        private ProductModel _selectedProduct;
        public ProductModel SelectedProduct
        {
            get => _selectedProduct;
            set
            {
                if (SetProperty(ref _selectedProduct, value))
                {
                    // 只要这个值变了（用户点了一行新数据），立刻触发加载 BOM 的方法！
                    LoadBomList(value);
                }
            }
        }

        private async void LoadBomList(ProductModel product)
        {
            if (product == null || product.Id <= 0)
            {
                CurrentBomList.Clear();
                return;
            }

            // 调用后端 API，传入产品 ID，查出它所有的 BOM 明细
            // var result = await _bomApi.GetBomByProductIdAsync(product.Id);
            // CurrentBomList = new ObservableCollection<BomItemModel>(result.Data);
            // ... (省略具体API调用代码)
        }
    }
}
