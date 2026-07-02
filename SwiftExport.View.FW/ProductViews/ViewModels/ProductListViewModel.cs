using GalaSoft.MvvmLight.Command;
using MateWpfThemes.FW.Controls;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.UiInterface;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.View.FW.Mvvm;
using SwiftExport.View.FW.ProductViews.Models;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace SwiftExport.View.FW.ProductViews.ViewModels
{
    public class ProductListViewModel : BaseListModel<ProductModel>
    {
        private readonly IProductApi _productApi;
        private readonly IDialogService _msg;

        public ProductListViewModel(IProductApi productApi, IDialogService msg)
        {
            _productApi = productApi;
            _msg = msg;
            PageChangeCommand = new RelayCommand<PageChangeRequest>(OnPageChange);
        }

        private int _pageIndex = 1;
        public int PageIndex
        {
            get => _pageIndex;
            set => SetProperty(ref _pageIndex, value);
        }

        private int _pageSize = 50;
        public int PageSize
        {
            get => _pageSize;
            set => SetProperty(ref _pageSize, value);
        }

        private int _totalCount;
        public int TotalCount
        {
            get => _totalCount;
            set => SetProperty(ref _totalCount, value);
        }

        public string SearchText { get; set; }
        public ProductColorDto SelectedColorFilter { get; set; }
        public CustomerDto SelectedCustomerFilter { get; set; }
        public CategorySelectionModel SelectedCategoryFilter { get; set; }

        public RelayCommand<PageChangeRequest> PageChangeCommand { get; }

        private async void OnPageChange(PageChangeRequest request)
        {
            if (IsDirty)
            {
                var confirm = System.Windows.MessageBox.Show("当前列表有未保存的数据，翻页会丢失，是否继续？", "未保存提示", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
                if (confirm != System.Windows.MessageBoxResult.Yes)
                {
                    return;
                }
            }

            PageIndex = request.NewIndex;
            PageSize = request.NewSize;
            await LoadDataAsync();
        }

        public async Task LoadDataAsync()
        {
            if (_productApi == null) return;
            var search = new ProductSearchRequest
            {
                BatchKeywords = SearchText?.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(k => k.Trim())
                    .Where(k => !string.IsNullOrEmpty(k))
                    .ToList(),
                ColorCodeUnique = SelectedColorFilter != null ? SelectedColorFilter.ColorCodeUnique : null,
                PageIndex = PageIndex,
                PageSize = PageSize
            };

            if (SelectedCategoryFilter != null && SelectedCategoryFilter.IncludedChildren != null && SelectedCategoryFilter.IncludedChildren.Any())
            {
                search.CategoryIds = SelectedCategoryFilter.IncludedChildren
                    .Where(x => x != null && x.Uid != Guid.Empty)
                    .Select(x => x.Uid)
                    .Distinct()
                    .ToList();
            }

            if (SelectedCustomerFilter != null && SelectedCustomerFilter.Uid != Guid.Empty)
            {
                search.CustomerIds = new System.Collections.Generic.List<Guid> { SelectedCustomerFilter.Uid };
            }

            var result = await _productApi.GetProductPagedListAsync(search);
            if (result.Success && result.Data != null)
            {
                var dtos = result.Data.Items.ToList();
                ReLoadRangeFromDtoList(dtos);

                TotalCount = result.Data.TotalCount;
            }
            else
            {
                await _msg.ShowErrorAsync(result.Message);
            }
        }

        public async Task BatchSaveAsync()
        {
            if (!IsDirty)
            {
                await _msg.ShowInfoAsync("没有更改");
                return;
            }

            if (!IsAllValid)
            {
                await _msg.ShowErrorAsync("存在校验不通过的记录");
                return;
            }

            var request = new BatchRequestDto<ProductDto>
            {
                AddList = Items.Where(x => x.State == ModelState.Added).Select(x => { var dto = new ProductDto(); Mapster.TypeAdapter.Adapt(x, dto); return dto; }).ToList(),
                UpdateList = Items.Where(x => x.State == ModelState.Modified).Select(x => { var dto = new ProductDto(); Mapster.TypeAdapter.Adapt(x, dto); return dto; }).ToList(),
                DeleteIdList = Items.Where(x => x.State == ModelState.Deleted).Select(x => x.Uid).ToList()
            };

            var rst = await _productApi.BatchSaveAsync(request);
            if (rst.Success)
            {
                await _msg.ShowInfoAsync("保存成功");
                await LoadDataAsync();
            }
            else
            {
                await _msg.ShowErrorAsync("保存失败: " + rst.Message);
            }
        }
    }
}