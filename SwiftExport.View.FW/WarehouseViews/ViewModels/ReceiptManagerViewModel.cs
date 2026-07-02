using GalaSoft.MvvmLight.Command;
using MateWpfThemes.FW.Controls;
using SwiftExport.Contracts.Apis.Orders;
using SwiftExport.Contracts.DTOs.Warehouses;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.UiInterface;
using SwiftExport.View.FW.DI;
using SwiftExport.View.FW.Mvvm;
using SwiftExport.View.FW.WarehouseViews.Models;
using SwiftExport.View.FW.WarehouseViews.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace SwiftExport.View.FW.WarehouseViews.ViewModels
{
    /// <summary>
    /// 入库单管理页 ViewModel。
    /// </summary>
    public class ReceiptManagerViewModel : ViewModelBaseEx
    {
        private readonly IReceiveApi _receiveApi;
        private readonly IDialogService _msg;


        public ReceiveHeadList HeadTable { get; set; }
        public ReceiveLineList LineTable { get; set; }

        public ObservableCollection<string> MaterialTypes { get; } = new ObservableCollection<string>();

        public ReceiptManagerViewModel(IReceiveApi receiveApi, IDialogService msg)
        {
            _receiveApi = receiveApi;
            _msg = msg;

            HeadTable = new ReceiveHeadList();
            LineTable = new ReceiveLineList();

            InitCommands();
            InitEnum();
            _ = LoadHeadDataAsync();
        }

        private void InitEnum()
        {
            MaterialTypes.Clear();
            MaterialTypes.Add(string.Empty); // 全部
            foreach (var item in MaterialTypesEnum.MaterialTypes)
            {
                MaterialTypes.Add(item);
            }
        }

        private async Task LoadHeadDataAsync()
        {
            using (BusyScope())
            {
                try
                {
                    StatusMessage = "正在加载入库单头数据...";
                    var req = GetSearchDtoFromFilter();
                    var rst = await _receiveApi.GetPagedListAsync(req);
                    if (rst.Success && rst.Data != null)
                    {
                        HeadTable.ReLoadRangeFromDtoList(rst.Data.Items.ToList());
                        TotalCount = rst.Data.TotalCount;
                        StatusMessage = $"加载成功，共 {TotalCount} 条";
                    }
                    else
                    {
                        StatusMessage = "加载失败：" + rst.Message;
                        await _msg.ShowWarningAsync(StatusMessage);
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = "加载异常：" + ex.Message;
                    await _msg.ShowErrorAsync(StatusMessage);
                }
            }
        }

        private async Task LoadLineDataAsync(Guid receiveHeadId)
        {
            using (BusyScope())
            {
                try
                {
                    StatusMessage = "正在加载入库明细...";
                    var rst = await _receiveApi.GetLinesByHeadIdAsync(receiveHeadId);
                    if (rst.Success && rst.Data != null)
                    {
                        LineTable.ReLoadRangeFromDtoList(rst.Data.ToList());
                        StatusMessage = $"明细加载成功，共 {LineTable.Items.Count} 行";
                    }
                    else
                    {
                        LineTable.Clear();
                        StatusMessage = "明细加载失败：" + rst.Message;
                    }
                }
                catch (Exception ex)
                {
                    LineTable.Clear();
                    StatusMessage = "明细加载异常：" + ex.Message;
                }
            }
        }

        private ReceiveHeadModel _selectedHeadModel;
        public ReceiveHeadModel SelectedHeadModel
        {
            get => _selectedHeadModel;
            set
            {
                if (Set(ref _selectedHeadModel, value))
                {
                    if (value != null)
                    {
                        _ = LoadLineDataAsync(value.Uid);
                    }
                    else
                    {
                        LineTable.Clear();
                    }
                }
            }
        }

        #region 筛选条件

        private string _filterSupplierCode;
        public string FilterSupplierCode
        {
            get => _filterSupplierCode;
            set => Set(ref _filterSupplierCode, value);
        }

        private string _filterText;
        public string FilterText
        {
            get => _filterText;
            set => Set(ref _filterText, value);
        }

        private bool _filterPosted;
        public bool FilterPosted
        {
            get => _filterPosted;
            set => Set(ref _filterPosted, value);
        }

        private DateTime? _filterFromDate;
        public DateTime? FilterFromDate
        {
            get => _filterFromDate;
            set => Set(ref _filterFromDate, value);
        }

        private DateTime? _filterToDate;
        public DateTime? FilterToDate
        {
            get => _filterToDate;
            set => Set(ref _filterToDate, value);
        }

        private string _filterMaterialType;
        public string FilterMaterialType
        {
            get => _filterMaterialType;
            set => Set(ref _filterMaterialType, value);
        }

        private string _filterPoNo;
        public string FilterPoNo
        {
            get => _filterPoNo;
            set => Set(ref _filterPoNo, value);
        }

        #endregion

        private string _statusMessage = "准备就绪";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public RelayCommand CreatReceiptDetailCommand { get; set; }
        public RelayCommand EditReceiptDetailCommand { get; set; }
        public RelayCommand DeleteReceiptDetailCommand { get; set; }
        public RelayCommand RefreshReceiptDetailCommand { get; set; }
        public RelayCommand PostReceiptDetailCommand { get; set; }
        public RelayCommand SearchCommand { get; set; }
        public RelayCommand ResetFilterCommand { get; set; }

        private void InitCommands()
        {
            CreatReceiptDetailCommand = new RelayCommand(CreatReceiptDetail);
            EditReceiptDetailCommand = new RelayCommand(EditReceiptDetail);
            DeleteReceiptDetailCommand = new RelayCommand(DeleteReceiptDetail);
            RefreshReceiptDetailCommand = new RelayCommand(RefreshReceiptDetail);
            PostReceiptDetailCommand = new RelayCommand(() => PostReceiptDetail());
            SearchCommand = new RelayCommand(() =>
            {
                PageIndex = 1;
                _ = LoadHeadDataAsync();
            });
            ResetFilterCommand = new RelayCommand(() =>
            {
                FilterSupplierCode = string.Empty;
                FilterText = string.Empty;
                FilterPosted = false;
                FilterFromDate = null;
                FilterToDate = null;
                FilterMaterialType = string.Empty;
                FilterPoNo = string.Empty;
                PageIndex = 1;
                _ = LoadHeadDataAsync();
            });
        }

        private async void CreatReceiptDetail()
        {
            try
            {
                var vm = IocProvider.GetService<ReceiptDetailViewModel>();
                vm.InitForCreate();

                var win = new Window
                {
                    Title = "新建入库单",
                    Width = 1300,
                    Height = 820,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Content = new ReceiptDetailView(vm)
                };

                // 订阅关闭事件
                vm.RequestClose += (s, result) =>
                {
                    if (result) win.DialogResult = true; // 设置 DialogResult 会自动关闭窗口
                    win.Close();
                };
                // 阻塞式打开
                if (win.ShowDialog() == true)
                {
                    // 窗口关闭后，直接从 vm 里拿数据
                    var finalResult = vm.ResultData;

                    if (finalResult == Guid.Empty) { return; }

                    var dto = await _receiveApi.GetHeadByIdAsync(finalResult);
                    if (dto.Success && dto.Data != null)
                    {
                        var newModel = new ReceiveHeadModel
                        {
                            Uid = dto.Data.Uid

                        };
                        newModel.LoadFromDto(dto.Data);
                        newModel.State = ModelState.Unchanged;
                        HeadTable.AddModelToFirst(newModel);
                    }
                }

            }
            catch (Exception ex)
            {
                await _msg.ShowErrorAsync("打开新建窗口失败：" + ex.Message);
            }
        }

        private async void EditReceiptDetail()
        {
            if (SelectedHeadModel == null)
            {
                await _msg.ShowInfoAsync("请先选择一条入库单。", "提示");
                return;
            }

            try
            {
                var vm = IocProvider.GetService<ReceiptDetailViewModel>();
                await vm.InitForEditAsync(SelectedHeadModel);

                var win = new Window
                {
                    Title = "编辑入库单",
                    Width = 1300,
                    Height = 820,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Content = new ReceiptDetailView(vm)
                };

                // 订阅关闭事件
                vm.RequestClose += (s, result) =>
                {
                    if (result) win.DialogResult = true; // 设置 DialogResult 会自动关闭窗口
                    win.Close();
                };
                // 阻塞式打开
                if (win.ShowDialog() == true)
                {
                    // 窗口关闭后，直接从 vm 里拿数据
                    var finalResult = vm.ResultData;

                    if (finalResult == Guid.Empty) { return; }

                    var dto = await _receiveApi.GetHeadByIdAsync(finalResult);
                    if (dto.Success && dto.Data != null)
                    {
                        SelectedHeadModel.LoadFromDto(dto.Data);
                    }
                }
                LineTable.Clear();
                if (SelectedHeadModel != null)
                    await LoadLineDataAsync(SelectedHeadModel.Uid);
            }
            catch (Exception ex)
            {
                await _msg.ShowErrorAsync("打开编辑窗口失败：" + ex.Message);
            }
        }

        private async void DeleteReceiptDetail()
        {
            await _msg.ShowInfoAsync("待后端提供删除接口后接入。", "功能预留");
        }

        private void RefreshReceiptDetail()
        {
            _ = LoadHeadDataAsync();
        }

        private async void PostReceiptDetail()
        {
            var checkedHeads = HeadTable.GetCheckedItems();
            var ids = checkedHeads.Count > 0
                ? checkedHeads.Select(x => x.Uid).Distinct().ToList()
                : (SelectedHeadModel != null ? new List<Guid> { SelectedHeadModel.Uid } : new List<Guid>());

            if (ids.Count == 0)
            {
                await _msg.ShowInfoAsync("请先勾选或选中要过账的入库单。", "提示");
                return;
            }

            var ok = await _msg.ShowConfirmAsync($"确认对 {ids.Count} 张入库单执行过账吗？", "确认过账");
            if (!ok) return;

            using (BusyScope())
            {
                try
                {
                    var rst = await _receiveApi.PostReceiveByHeadIdListAsync(ids);
                    if (rst.Success)
                    {
                        StatusMessage = "过账成功";
                        await _msg.ShowInfoAsync(StatusMessage);
                        await LoadHeadDataAsync();
                        if (SelectedHeadModel != null)
                            await LoadLineDataAsync(SelectedHeadModel.Uid);
                    }
                    else
                    {
                        StatusMessage = "过账失败：" + rst.Message;
                        await _msg.ShowWarningAsync(StatusMessage);
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = "过账异常：" + ex.Message;
                    await _msg.ShowErrorAsync(StatusMessage);
                }
            }
        }

        #region 翻页

        private int _pageIndex = 1;
        public int PageIndex
        {
            get => _pageIndex;
            set
            {
                if (_pageIndex == value) return;
                if (SetProperty(ref _pageIndex, value))
                {
                    _ = LoadHeadDataAsync();
                    LineTable.Clear();
                }
            }
        }

        private int _pageSize = 20;
        public int PageSize
        {
            get => _pageSize;
            set
            {
                if (_pageSize == value) return;
                if (SetProperty(ref _pageSize, value))
                {
                    if (PageIndex == 1)
                    {
                        _ = LoadHeadDataAsync();
                        LineTable.Clear();
                    }
                    else
                    {
                        PageIndex = 1;
                    }
                }
            }
        }

        private int _totalCount;
        public int TotalCount
        {
            get => _totalCount;
            set => SetProperty(ref _totalCount, value);
        }

        public ICommand PageChangeCommand => new RelayCommand<PageChangeRequest>(async req =>
        {
            if (HeadTable.IsDirty)
            {
                var ok = await _msg.ShowConfirmAsync("当前有未保存修改，翻页将丢失，是否继续？", "提示");
                if (!ok) return;
            }

            PageSize = req.NewSize;
            PageIndex = req.NewIndex;
        });

        #endregion

        #region 辅助函数

        private ReceiveOrderSearchDto GetSearchDtoFromFilter()
        {
            return new ReceiveOrderSearchDto
            {
                SupplierCode = string.IsNullOrWhiteSpace(FilterSupplierCode) ? null : FilterSupplierCode.Trim(),
                Keyword = string.IsNullOrWhiteSpace(FilterText) ? null : FilterText.Trim(),
                // 根据 TextBox(FilterText) 里的换行符分割成多个关键字
                BatchKeywords = string.IsNullOrWhiteSpace(FilterText)
                    ? null
                    : FilterText
                        .Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Trim())
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                IsPosted = FilterPosted,
                ReceiveDateFrom = FilterFromDate,
                ReceiveDateTo = FilterToDate,
                PoNo = string.IsNullOrWhiteSpace(FilterPoNo) ? null : FilterPoNo.Trim(),
                PageIndex = PageIndex,
                PageSize = PageSize
            };
        }

        #endregion
    }
}
