using GalaSoft.MvvmLight.Command;
using Mapster;
using SwiftExport.Contracts.Apis.CustomerApis;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.UiInterface;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.View.FW.CustomerViews.Enums;
using SwiftExport.View.FW.CustomerViews.Models;
using SwiftExport.View.FW.Mvvm;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace SwiftExport.View.FW.CustomerViews.ViewModels
{
    public class CustomerManagerViewModel : ViewModelBaseEx
    {
        private readonly IDialogService _msg;
        private readonly ICustomerApi _customerApi;
        private readonly ICustomerProductApi _customerProductApi;

        private readonly DispatcherTimer _搜索客户防抖计时器;
        private readonly DispatcherTimer _搜索客户产品防抖计时器;


        public CustomerManagerViewModel(ICustomerApi customerApi, ICustomerProductApi customerProductApi, IDialogService dialogService)
        {

            _customerApi = customerApi;
            _customerProductApi = customerProductApi;
            _msg = dialogService;

            CurrentCustomerTable = new CustomerList();
            CurrentProductTable = new CustomerProductList();

            AddCustomerCommand = new RelayCommand(AddCustomer);
            BatchSaveCustomerAsyncCommand = new RelayCommand(async () => await BatchSaveCustomerAsync(), () => CanBatchSaveCustomerAsync);
            DeleteCustomerAsyncCommand = new RelayCommand(async () => await DeleteCustomerAsync(), () => CanDeleteCustomer);
            LoadCustomerAsyncCommand = new RelayCommand(async () => await LoadCustomerAsync());
            InvertCheckCommand = new RelayCommand(InvertCheck);

            // 监听客户表的状态变化（脏数据、勾选数量等）
            CurrentCustomerTable.ListStateChanged += () =>
            {
                RaisePropertyChanged(nameof(CanBatchSaveCustomerAsync));
                BatchSaveCustomerAsyncCommand.RaiseCanExecuteChanged();
                RaisePropertyChanged(nameof(CanDeleteCustomer));
                DeleteCustomerAsyncCommand.RaiseCanExecuteChanged();
            };

            _搜索客户防抖计时器 = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };// 800毫秒的输入防抖，用户停止输入800ms后才会触发搜索
            _搜索客户防抖计时器.Tick += 搜索客户防抖计时器_Tick;


            AddProductCommand = new RelayCommand(AddProduct);
            BatchSaveProductAsyncCommand = new RelayCommand(async () => await BatchSaveProductAsync(), () => CanBatchSaveProductAsync);
            DeleteProductAsyncCommand = new RelayCommand(async () => await DeleteProductAsync(), () => CanDeleteProduct);
            CheckAndLoadProductCommand = new RelayCommand(CheckAndLoadProduct);
            InvertCheckProductCommand = new RelayCommand(InvertCheckProduct);

            // 监听产品表的状态变化（脏数据、有效性等）
            CurrentProductTable.ListStateChanged += () =>
            {
                RaisePropertyChanged(nameof(CanBatchSaveProductAsync));
                BatchSaveProductAsyncCommand.RaiseCanExecuteChanged();
                RaisePropertyChanged(nameof(CanDeleteProduct));
                DeleteProductAsyncCommand.RaiseCanExecuteChanged();
            };

            _搜索客户产品防抖计时器 = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };// 800毫秒的输入防抖，用户停止输入800ms后才会触发搜索
            _搜索客户产品防抖计时器.Tick += 搜索客户产品防抖计时器_Tick;

            _ = LoadCustomerAsync();

        }

        #region 字段属性
        private string _statusMessage = "准备就绪";// UI底部的状态提示信息，绑定到UI的状态栏，用于显示当前操作状态、错误信息等，由各个操作方法在执行过程中设置以反馈给用户
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }
        #region Tab控制相关字段
        private CustomerManagerTabs _activeTab;
        public CustomerManagerTabs ActiveTab
        {
            get => _activeTab;
            set
            {
                if (SetProperty(ref _activeTab, value))
                {
                    if (value == CustomerManagerTabs.CustomerProducts)
                        CheckAndLoadProduct();
                    if (value == CustomerManagerTabs.CustomerInfo)
                        StatusMessage = "";
                }

            }
        }

        #endregion

        #region 客户表字段
        public CustomerList CurrentCustomerTable { get; }
        private CustomerModel _selectedCustomer;
        public CustomerModel SelectedCustomer
        {
            get => _selectedCustomer;
            set
            {
                if (SetProperty(ref _selectedCustomer, value))
                {
                    // 选中客户变化时，刷新相关按钮可用性
                    RaisePropertyChanged(nameof(HasSelectedCustomer));
                    RaisePropertyChanged(nameof(CanDeleteCustomer));
                    DeleteCustomerAsyncCommand.RaiseCanExecuteChanged();
                    DeleteProductAsyncCommand.RaiseCanExecuteChanged();

                    // 清空产品列表，等用户切换到产品 Tab 再重新加载，避免不必要的 API 请求
                    CurrentProductTable.Clear();
                    TotalProductTableCount = 0;
                }
            }
        }

        /// <summary>
        /// 是否选择了客户（用于控制"客户产品管理"标签页的操作按钮可用性，要求已持久化的客户 Id > 0）
        /// </summary>
        public bool HasSelectedCustomer => _selectedCustomer != null && _selectedCustomer.Id > 0;


        private string _searchCustomerText;
        public string SearchCustomerText
        {
            get => _searchCustomerText;
            set
            {
                if (SetProperty(ref _searchCustomerText, value))
                {
                    防抖搜索客户开始();
                }
            }
        }

        #endregion

        #region 客户表分页控件

        private int _pageCustomerTableIndex = 1;// 当前页码，绑定到UI的分页控件，用户切换页码时会触发数据重新加载以显示对应页的数据
        public int PageCustomerTableIndex
        {
            get => _pageCustomerTableIndex;
            set
            {
                if (SetProperty(ref _pageCustomerTableIndex, value))
                {
                    if (IsBusy) return;
                    _ = LoadCustomerAsync();
                }
            }
        }

        private int _pageCustomerTableSize = 50;// 每页行数，绑定到UI的分页控件，用户修改每页行数时会触发数据重新加载以应用新的分页设置
        public int PageCustomerTableSize
        {
            get => _pageCustomerTableSize;
            set
            {
                if (SetProperty(ref _pageCustomerTableSize, value))
                {
                    if (IsBusy) return;
                    if (PageCustomerTableIndex == 1) _ = LoadCustomerAsync();
                    else PageCustomerTableIndex = 1;
                }
            }
        }

        private int _totalCustomerTableCount;// 数据总条数，绑定到UI的分页控件，用于显示总页数和当前数据范围等信息，由数据加载方法在获取数据后设置
        public int TotalCustomerTableCount
        {
            get => _totalCustomerTableCount;
            set => SetProperty(ref _totalCustomerTableCount, value);
        }

        #endregion

        #region 客户产品表字段
        public CustomerProductList CurrentProductTable { get; }
        private CustomerProductModel _selectedProduct;
        public CustomerProductModel SelectedProduct
        {
            get => _selectedProduct;
            set
            {
                if (SetProperty(ref _selectedProduct, value))
                {
                    // 选中行变化时，刷新删除按钮可用性
                    RaisePropertyChanged(nameof(CanDeleteProduct));
                    DeleteProductAsyncCommand.RaiseCanExecuteChanged();
                }
            }
        }
        public bool ToggSelectionUnit => !ToggSelectionUnit;

        private string _searchProductText;// 主表格的搜索关键字，绑定到UI的搜索框，用户输入时会触发防抖机制以减少频繁查询，并在停止输入后重新加载数据以应用搜索过滤
        public string SearchProductText
        {
            get => _searchProductText;
            set
            {
                if (SetProperty(ref _searchProductText, value))
                {
                    if (IsBusy) return;

                    防抖搜索客户产品开始();

                }
            }
        }


        #region 客户产品表分页控件

        private int _pageProductTableIndex = 1;// 当前页码，绑定到UI的分页控件，用户切换页码时会触发数据重新加载以显示对应页的数据
        public int PageProductTableIndex
        {
            get => _pageProductTableIndex;
            set
            {
                if (SetProperty(ref _pageProductTableIndex, value))
                {
                    if (IsBusy) return;
                    CheckAndLoadProduct();
                }
            }
        }

        private int _pageProductTableSize = 50;// 每页行数，绑定到UI的分页控件，用户修改每页行数时会触发数据重新加载以应用新的分页设置
        public int PageProductTableSize
        {
            get => _pageProductTableSize;
            set
            {
                if (SetProperty(ref _pageProductTableSize, value))
                {
                    if (IsBusy) return;
                    if (PageProductTableIndex == 1) CheckAndLoadProduct();
                    else PageProductTableIndex = 1;
                }
            }
        }

        private int _totalProductTableCount;// 数据总条数，绑定到UI的分页控件，用于显示总页数和当前数据范围等信息，由数据加载方法在获取数据后设置
        public int TotalProductTableCount
        {
            get => _totalProductTableCount;
            set => SetProperty(ref _totalProductTableCount, value);
        }

        #endregion



        #endregion


        #endregion

        #region 命令
        //==============
        //客户表命令
        //==============
        #region 客户表命令


        //新增客户命令

        public RelayCommand AddCustomerCommand { get; }
        private void AddCustomer()
        {
            using (BusyScope())
            {
                var model = new CustomerModel() { CustomerCode = "Newcode" };
                model.MarkAsAdded();
                CurrentCustomerTable.AddModelToFirst(model);
                StatusMessage = "已添加新客户行，请编辑相关信息并保存。";
            }

        }

        /// <summary>
        /// 客户表是否有脏数据
        /// </summary>
        public bool CanBatchSaveCustomerAsync => CurrentCustomerTable.IsDirty;

        //批量保存客户命令
        public RelayCommand BatchSaveCustomerAsyncCommand { get; }
        private async Task BatchSaveCustomerAsync()
        {
            var dirtyModels = CurrentCustomerTable.GetChangeSet();
            if (dirtyModels.AddList.Count == 0 && dirtyModels.UpdateList.Count == 0 && dirtyModels.DeleteIdList.Count == 0)
            {
                StatusMessage = "没有需要保存的更改。";
                return;
            }
            if (CurrentCustomerTable.IsAllValid == false)
            {
                StatusMessage = "请修正数据中的错误后再保存。";
                return;
            }
            StatusMessage = "正在保存更改...";

            var req = dirtyModels.Adapt<BatchRequestGeneric<CustomerDto>>(); // 先调用Adapt方法进行类型转换，确保所有属性都被正确映射
            try
            {
                var result = await _customerApi.BatchSaveAsync(req);
                if (result.Success)
                {
                    StatusMessage = "保存成功。";

                    await _msg.ShowInfoAsync("保存成功！");
                    _ = LoadCustomerAsync();
                }
                else
                {
                    StatusMessage = "保存失败: " + result.Message;
                    return;
                }
            }
            catch (Exception ex)
            {
                await _msg.ShowErrorAsync("异常: " + ex.Message);
            }
        }



        /// <summary>
        /// 客户表删除命令是否可用：有勾选项 或 有当前选中行，任意一个满足即可
        /// </summary>
        public bool CanDeleteCustomer => CurrentCustomerTable.CheckedCount > 0 || _selectedCustomer != null;

        public RelayCommand DeleteCustomerAsyncCommand { get; }
        private async Task DeleteCustomerAsync()
        {
             if (SelectedCustomer != null && SelectedCustomer.Id<=0) 
                { 
                    CurrentCustomerTable.Remove(SelectedCustomer); 
                }

            var checkItems = CurrentCustomerTable.GetCheckedItems();

            if (checkItems.Count == 0) return;

            StatusMessage = $"准备删除 {checkItems.Count} 条记录...";
            try
            {
                //删除刚刚新建的行
                var deleteByNew = checkItems.Where(x => x.Id <= 0).ToList();
                foreach (var item in deleteByNew) { CurrentCustomerTable.Remove(item); }

                //发API删除数据库的行
                var apiToDelete = checkItems.Where(x => x.Id > 0).ToList();
                if (apiToDelete.Count > 0)
                {
                    if (!await _msg.ShowConfirmAsync($"确定要删除选中的 {apiToDelete.Count} 条记录吗？", "确认删除")) return;

                    bool isSuccess = false;
                    string errMsg = "";

                    if (apiToDelete.Count == 1)
                    {
                        var rst = await _customerApi.DeleteAsync(apiToDelete.First().Id);
                        isSuccess = rst.Success;
                        errMsg = rst.Message;
                    }
                    else
                    {
                        var req = new BatchRequestGeneric<CustomerDto> { DeleteIdList = apiToDelete.Select(x => x.Id).ToList() };
                        var rst = await _customerApi.BatchSaveAsync(req);
                        isSuccess = rst.Success;
                        errMsg = rst.Message;
                    }
                    if (isSuccess)
                    {
                        foreach (var item in apiToDelete) CurrentCustomerTable.Delete(item);
                        await _msg.ShowInfoAsync("删除成功！");
                    }
                    else
                    {
                        await _msg.ShowErrorAsync("删除失败：" + errMsg);
                        return;
                    }
                }

            }
            catch (Exception ex) { await _msg.ShowErrorAsync("删除异常：" + ex.Message); }
            finally
            {
                StatusMessage = "准备就绪";
            }
        }
        public RelayCommand LoadCustomerAsyncCommand { get; }
        private async Task LoadCustomerAsync()
        {
            if (_customerApi == null) return;

            using (BusyScope())
            {
                try
                {
                    var requestDto = new PagedRequestDto
                    {
                        SearchText = SearchCustomerText,
                        // 绑定到当前页面索引
                        PageIndex = PageCustomerTableIndex,
                        // 绑定到当前的页面行数(PageCustomerTableSize)
                        PageSize = PageCustomerTableSize
                    };

                    var result = await _customerApi.GetPagedAsync(requestDto);

                    DispatcherProvider.BeginInvoke(() =>
                    {
                        if (result.Success && result.Data != null)
                        {

                            var dtos = result.Data.Items.ToList();

                            CurrentCustomerTable.ReLoadRangeFromDtoList(dtos);

                            TotalCustomerTableCount = dtos.Count;
                            StatusMessage = $"加载成功，共 {TotalCustomerTableCount} 条。";
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
                finally
                {
                }
            }
        }
        //反选命令
        public RelayCommand InvertCheckCommand { get; private set; }
        private void InvertCheck()
        {
            CurrentCustomerTable.InvertCheckAll();
        }

        private void 防抖搜索客户开始()
        {
            _搜索客户防抖计时器.Stop();
            _搜索客户防抖计时器.Start();
        }

        private void 搜索客户防抖计时器_Tick(object sender, EventArgs e)
        {
            _搜索客户防抖计时器.Stop();

            if (IsBusy) return;

            if (PageCustomerTableIndex != 1)
            {
                PageCustomerTableIndex = 1;
            }

            _ = LoadCustomerAsync();
        }



        #endregion

        #region 客户产品表命令

        public RelayCommand AddProductCommand { get; }
        private void AddProduct()
        {
            if (SelectedCustomer == null)
            { StatusMessage = "未选择客户"; return; }

            var model = new CustomerProductModel()
            {
                CustomerId = SelectedCustomer.Id,
                
                CustomerSku = "新客户编码",
                Sku = "aksldjlak"
            };
            model.MarkAsAdded();
            CurrentProductTable.AddModelToFirst(model);
            StatusMessage = "已添加新客户行，请编辑相关信息并保存。";
        }
        /// <summary>
        /// 产品表是否有脏数据
        /// </summary>
        public bool CanBatchSaveProductAsync => CurrentProductTable.IsDirty;
        //批量保存客户命令
        public RelayCommand BatchSaveProductAsyncCommand { get; }
        private async Task BatchSaveProductAsync()
        {
            var dirtyModels = CurrentProductTable.GetChangeSet();
            if (dirtyModels.AddList.Count == 0 && dirtyModels.UpdateList.Count == 0 && dirtyModels.DeleteIdList.Count == 0)
            {
                StatusMessage = "没有需要保存的更改。";
                return;
            }
            if (CurrentProductTable.IsAllValid == false)
            {
                StatusMessage = "请修正数据中的错误后再保存。";
                return;
            }
            StatusMessage = "正在保存更改...";

            var req = dirtyModels.Adapt<BatchRequestGeneric<CustomerProductDto>>(); // 先调用Adapt方法进行类型转换，确保所有属性都被正确映射
            try
            {
                var result = await _customerProductApi.BatchSaveAsync(req);
                if (result.Success)
                {
                    StatusMessage = "保存成功。";

                    await _msg.ShowInfoAsync("保存成功！");
                    CheckAndLoadProduct();
                }
                else
                {
                    StatusMessage = "保存失败: " + result.Message;
                    return;
                }
            }
            catch (Exception ex)
            {
                await _msg.ShowErrorAsync("异常: " + ex.Message);
            }
        }
        //删除客户产品产命令
        public bool CanDeleteProduct => CurrentProductTable.CheckedCount > 0 || _selectedProduct != null;
        public RelayCommand DeleteProductAsyncCommand { get; }
        private async Task DeleteProductAsync()
        {
            var checkItems = CurrentProductTable.GetCheckedItems();

            if (checkItems.Count == 0) return;

            StatusMessage = $"准备删除 {checkItems.Count} 条记录...";
            try
            {
                //删除刚刚新建的行
                var deleteByNew = checkItems.Where(x => x.Id <= 0).ToList();
                foreach (var item in deleteByNew) { CurrentProductTable.Remove(item); }

                //发API删除数据库的行
                var apiToDelete = checkItems.Where(x => x.Id > 0).ToList();
                if (apiToDelete.Count > 0)
                {
                    if (!await _msg.ShowConfirmAsync($"确定要删除选中的 {apiToDelete.Count} 条记录吗？", "确认删除")) return;

                    bool isSuccess = false;
                    string errMsg = "";

                    if (apiToDelete.Count == 1)
                    {
                        var rst = await _customerProductApi.DeleteAsync(apiToDelete.First().Id);
                        isSuccess = rst.Success;
                        errMsg = rst.Message;
                    }
                    else
                    {
                        var req = new BatchRequestGeneric<CustomerProductDto> { DeleteIdList = apiToDelete.Select(x => x.Id).ToList() };
                        var rst = await _customerProductApi.BatchSaveAsync(req);
                        isSuccess = rst.Success;
                        errMsg = rst.Message;
                    }
                    if (isSuccess)
                    {
                        foreach (var item in apiToDelete) CurrentProductTable.Delete(item);
                        await _msg.ShowInfoAsync("删除成功！");
                    }
                    else
                    {
                        await _msg.ShowErrorAsync("删除失败：" + errMsg);
                        return;
                    }
                }

            }
            catch (Exception ex) { await _msg.ShowErrorAsync("删除异常：" + ex.Message); }
            finally
            {
                StatusMessage = "准备就绪";
            }
        }

        //反选命令
        public RelayCommand InvertCheckProductCommand { get; private set; }
        private void InvertCheckProduct()
        {
            CurrentProductTable.InvertCheckAll();
        }
        public RelayCommand CheckAndLoadProductCommand { get;}

        private void CheckAndLoadProduct()
        {
            if (ActiveTab != CustomerManagerTabs.CustomerProducts) return;
            if (SelectedCustomer == null)
            {
                StatusMessage = "请先选择一个客户以查看其产品信息。";
                return;
            }
            _ = LoadCustomerProductAsync();
        }

        private async Task LoadCustomerProductAsync()
        {
            if (_customerProductApi == null) return;
            if (SelectedCustomer == null)
            {
                StatusMessage = "请先选择一个客户以查看其产品信息。";
                return;
            }
            var customerId = SelectedCustomer.Id;

            using (BusyScope())
            {
                try
                {
                    var requestDto = new ProductSearchDto
                    {
                         Keyword = SearchProductText,
                         CustomerIds = new List<int> { customerId },
                        // 绑定到当前页面索引
                        PageIndex = PageProductTableIndex,
                        // 绑定到当前的页面行数(PageProductTableSize)
                        PageSize = PageProductTableSize
                    };

                    var result = await _customerProductApi.GetCustomerProductPagedListAsync(requestDto);

                    DispatcherProvider.BeginInvoke(() =>
                    {
                        if (result.Success && result.Data != null)
                        {

                            var dtos = result.Data.Items.ToList();

                            CurrentProductTable.ReLoadRangeFromDtoList(dtos);

                            TotalProductTableCount = dtos.Count;
                            StatusMessage = $"加载客户产品成功，共 {TotalProductTableCount} 条。";
                            return;
                        }
                        else
                        {
                            StatusMessage = "加载客户产品失败: " + result.Message;
                        }
                    });
                }
                catch (Exception ex)
                {
                    StatusMessage = "加载客户产品异常: " + ex.Message;
                }
                finally
                {
                }
            }
        }

        private void 防抖搜索客户产品开始()
        {
            _搜索客户产品防抖计时器.Stop();
            _搜索客户产品防抖计时器.Start();
        }

        private void 搜索客户产品防抖计时器_Tick(object sender, EventArgs e)
        {
            _搜索客户产品防抖计时器.Stop();

            if (IsBusy) return;

            if (PageCustomerTableIndex != 1)
            {
                PageCustomerTableIndex = 1;
            }
            _ = LoadCustomerProductAsync();
        }

        #endregion




        #endregion






    }
}