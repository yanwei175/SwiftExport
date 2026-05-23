using GalaSoft.MvvmLight.Command;
using GalaSoft.MvvmLight.Messaging;
using SwiftExport.Contracts.Apis.Orders;
using SwiftExport.Contracts.Apis.SupplierApis;
using SwiftExport.Contracts.DTOs.Orders;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.View.FW.Mvvm;
using SwiftExport.View.FW.SupplierViews.Models;
using SwiftExport.View.FW.WarehouseViews.Messages;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SwiftExport.View.FW.WarehouseViews.ViewModels
{
    /// <summary>
    /// 待入库穿梭窗口 ViewModel。
    /// 目标：
    /// 1) 左侧按供应商 + 物料类型加载待入库候选行；
    /// 2) 穿梭选择到右侧；
    /// 3) 确认后返回最终入库行列表。
    /// </summary>
    public class AwatingInboundViewModel : ViewModelBaseEx
    {
        private readonly IReceiveApi _receiveApi;
        private readonly IPurchaseOrderApi _purchaseOrderApi;
        private CancellationTokenSource _searchCts;

        private int _currentPage = 1;
        private readonly int _pageSize = 100;
        private bool _hasMoreData = true;

        /// <summary>
        /// 通知 View 将左侧 DataGrid 滚动到顶部。
        /// </summary>
        public event Action RequestScrollToTop;
        /// <summary> 弹窗后的结果</summary>
        public List<AwatingInboundDto> ResultData { get; } = new List<AwatingInboundDto>();
        /// <summary> 关闭弹窗的委托 </summary>
        public event EventHandler<bool> RequestClose;

        public AwatingInboundViewModel(IReceiveApi receiveApi, IPurchaseOrderApi purchaseOrderApi)
        {
            _receiveApi = receiveApi;
            _purchaseOrderApi = purchaseOrderApi;

            SupplierList = new ObservableCollection<SupplierDto>();

            AwatingInboundList = new ObservableCollection<AwatingInboundDto>();
            SelectedInboundLines = new ObservableCollection<AwatingInboundDto>();

            TempSelectedSourceItems = new List<AwatingInboundDto>();
            TempSelectedTargetItems = new List<AwatingInboundDto>();


            _selectedMaterialType = MaterialTypesEnum.客供件;

            LoadDataCommand = new RelayCommand(async () => await LoadDataAsync(false));
            SelectItemsCommand = new RelayCommand(SelectItems);
            SelectAllCommand = new RelayCommand(SelectAll);
            RemoveItemsCommand = new RelayCommand(RemoveItems);
            RemoveAllCommand = new RelayCommand(RemoveAll);
            ConfirmCommand = new RelayCommand(Confirm);
            CancelCommand = new RelayCommand(Cancel);

            _ = LoadSuppliersAsync();
        }

        public RelayCommand LoadDataCommand { get; }
        public RelayCommand SelectItemsCommand { get; }
        public RelayCommand SelectAllCommand { get; }
        public RelayCommand RemoveItemsCommand { get; }
        public RelayCommand RemoveAllCommand { get; }
        public RelayCommand ConfirmCommand { get; }
        public RelayCommand CancelCommand { get; }

        public ObservableCollection<SupplierDto> SupplierList { get; }

        public List<string> MaterialTypes { get; }= new List<string> {""}.Concat(MaterialTypesEnum.MaterialTypes).ToList();

        public ObservableCollection<AwatingInboundDto> AwatingInboundList { get; }
        public ObservableCollection<AwatingInboundDto> SelectedInboundLines { get; }

        public List<AwatingInboundDto> TempSelectedSourceItems { get; set; }
        public List<AwatingInboundDto> TempSelectedTargetItems { get; set; }

        private string _selectedSupplierCode;
        public string SelectedSupplierCode
        {
            get => _selectedSupplierCode;
            set
            {
                if (SetProperty(ref _selectedSupplierCode, value))
                    TriggerSearch();
            }
        }

        private string _selectedMaterialType;
        public string SelectedMaterialType
        {
            get => _selectedMaterialType;
            set
            {
                if (SetProperty(ref _selectedMaterialType, value))
                    TriggerSearch();
            }
        }

        private string _statusMessage = "请选择供应商后加载待入库数据";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private async Task LoadSuppliersAsync()
        {
            try
            {
                StatusMessage = "正在加载供应商列表...";
                var rst = await _purchaseOrderApi.GetAwatingInboudSuppliersAsync();
                if (rst != null && rst.Success && rst.Data != null)
                {
                    DispatcherProvider.Invoke(() =>
                    {
                        SupplierList.Clear();
                        foreach (var s in rst.Data)
                            SupplierList.Add(s);
                    });

                    StatusMessage = "准备就绪";
                }
                else
                {
                    StatusMessage = "加载供应商失败：" + (rst?.Message ?? "未知错误");
                }
            }
            catch (Exception ex)
            {
                StatusMessage = "加载供应商异常：" + ex.Message;
            }
        }

        private void TriggerSearch()
        {
            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();

            Task.Delay(400, _searchCts.Token).ContinueWith(t =>
            {
                if (t.IsCanceled) return;
                DispatcherProvider.Invoke(() =>
                {
                    _ = LoadDataAsync(true);
                });
            }, TaskScheduler.Default);
        }

        private async Task LoadDataAsync(bool resetPage)
        {
            if (IsBusy) return;

            if (string.IsNullOrWhiteSpace(SelectedSupplierCode))
            {
                StatusMessage = "请先选择供应商";
                return;
            }

            if (resetPage)
            {
                DispatcherProvider.Invoke(() =>
                {
                    _currentPage = 1;
                    _hasMoreData = true;
                    AwatingInboundList.Clear();
                    RequestScrollToTop?.Invoke();
                });
            }

            if (!_hasMoreData) return;

            StatusMessage = "正在加载待入库数据...";

            using (BusyScope())
            {
                try
                {
                    var rst = await _receiveApi.GetAwatingInboundLinesAsync(SelectedSupplierCode, SelectedMaterialType);
                    if (!rst.Success || rst.Data == null)
                    {
                        StatusMessage = "加载失败：" + rst.Message;
                        return;
                    }

                    var all = rst.Data.ToList();
                    var page = all.Skip((_currentPage - 1) * _pageSize).Take(_pageSize).ToList();

                    DispatcherProvider.Invoke(() =>
                    {
                        foreach (var x in page)
                        {
                            AwatingInboundList.Add(x);
                        }
                    });

                    _hasMoreData = (_currentPage * _pageSize) < all.Count;
                    if (_hasMoreData) _currentPage++;

                    StatusMessage = $"已加载 {AwatingInboundList.Count} 行";
                }
                catch (Exception ex)
                {
                    StatusMessage = "加载异常：" + ex.Message;
                }
            }
        }

        private void SelectItems()
        {
            var selected = TempSelectedSourceItems;
            if (selected == null || !selected.Any()) return;

            foreach (var item in selected)
            {
                if (!SelectedInboundLines.Any(x => x.PoLineId == item.PoLineId))
                {
                    SelectedInboundLines.Add(item);
                }
            }
        }

        private void SelectAll()
        {
            foreach (var item in AwatingInboundList)
            {
                if (!SelectedInboundLines.Any(x => x.PoLineId == item.PoLineId))
                {
                    SelectedInboundLines.Add(item);
                }
            }
        }

        private void RemoveItems()
        {
            var selected = TempSelectedTargetItems;
            if (selected == null || !selected.Any()) return;

            foreach (var item in selected.ToList())
            {
                SelectedInboundLines.Remove(item);
            }
        }

        private void RemoveAll()
        {
            SelectedInboundLines.Clear();
        }

        private void Confirm()
        {
            if (SelectedInboundLines.Count == 0)
            {
                if (TempSelectedSourceItems != null && TempSelectedSourceItems.Any())
                {
                    foreach (var item in TempSelectedSourceItems)
                    {
                        if (!SelectedInboundLines.Any(x => x.PoLineId == item.PoLineId))
                            SelectedInboundLines.Add(item);
                    }
                }
                else if (AwatingInboundList.Count > 0)
                {
                    SelectedInboundLines.Add(AwatingInboundList.First());
                }
                else
                {
                    StatusMessage = "没有可确认的待入库行";
                    return;
                }
            }

            ResultData.Clear();
            foreach (var x in SelectedInboundLines)
                ResultData.Add(x);

            RequestClose?.Invoke(this, true);
        }

        private void Cancel()
        {
            ResultData.Clear();
            RequestClose?.Invoke(this, true);
        }
    }
}
