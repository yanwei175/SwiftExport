using GalaSoft.MvvmLight.Command;
using Mapster;
using MateWpfThemes.FW.Controls;
using SwiftExport.Contracts.Apis.Orders;
using SwiftExport.Contracts.Apis.SalesOrderApis;
using SwiftExport.Contracts.DTOs.PackingLists;
using SwiftExport.Contracts.DTOs.SalesOrders;
using SwiftExport.Contracts.UiInterface;
using SwiftExport.View.FW.Mvvm;
using SwiftExport.View.FW.PackingListViews.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SwiftExport.View.FW.PackingListViews.ViewModels
{
    public class PackingListManagerViewModel : ViewModelBaseEx
    {
        private readonly IPackingListApi _packingListApi;
        private readonly IPurchaseOrderApi _purchaseOrderApi;
        private readonly ISalesOrderApi _salesOrderApi;
        private readonly IDialogService _dialogService;

        public PackingListManagerViewModel() : this(null, null, null, null)
        {
        }

        public PackingListManagerViewModel(
            IPackingListApi packingListApi,
            IPurchaseOrderApi purchaseOrderApi,
            ISalesOrderApi salesOrderApi,
            IDialogService dialogService)
        {
            _packingListApi = packingListApi;
            _purchaseOrderApi = purchaseOrderApi;
            _salesOrderApi = salesOrderApi;
            _dialogService = dialogService;

            HeaderItems = new PackingListHeadList();
            LineItems = new PackingListLineList();



            LoadCommand = new RelayCommand(async () => await LoadAsync(), () => IsNotBusy);
            SaveCommand = new RelayCommand(async () => await SaveAsync(), () => IsNotBusy);
            PostCommand = new RelayCommand(async () => await PostAsync(), () => IsNotBusy && SelectedHeader != null);
            ImportPurchaseOrderCommand = new RelayCommand(async () => await ImportPurchaseOrderAsync(), () => IsNotBusy);
            ImportSalesOrderCommand = new RelayCommand(async () => await ImportSalesOrderAsync(), () => IsNotBusy);
            AddNewCommand = new RelayCommand(AddNew, () => IsNotBusy);
            RemoveSelectedLineCommand = new RelayCommand(RemoveSelectedLine, () => IsNotBusy && SelectedLine != null);

            RegisterBusyNotifyCommand(LoadCommand);
            RegisterBusyNotifyCommand(SaveCommand);
            RegisterBusyNotifyCommand(PostCommand);
            RegisterBusyNotifyCommand(ImportPurchaseOrderCommand);
            RegisterBusyNotifyCommand(ImportSalesOrderCommand);
            RegisterBusyNotifyCommand(AddNewCommand);
            RegisterBusyNotifyCommand(RemoveSelectedLineCommand);

            _ = LoadAsync();
        }

        public PackingListHeadList HeaderItems { get; private set; }
        public PackingListLineList LineItems { get; private set; }

        private PackingListHeadModel _selectedHeader;
        public PackingListHeadModel SelectedHeader
        {
            get { return _selectedHeader; }
            set
            {
                if (SetProperty(ref _selectedHeader, value))
                {
                    PostCommand.RaiseCanExecuteChanged();
                    _ = LoadDetailAsync();
                }
            }
        }

        private PackingListLineModel _selectedLine;
        public PackingListLineModel SelectedLine
        {
            get { return _selectedLine; }
            set
            {
                if (SetProperty(ref _selectedLine, value))
                    RemoveSelectedLineCommand.RaiseCanExecuteChanged();
            }
        }

        private string _searchText;
        public string SearchText
        {
            get { return _searchText; }
            set { SetProperty(ref _searchText, value); }
        }

        private string _sourcePoNo;
        public string SourcePoNo
        {
            get { return _sourcePoNo; }
            set { SetProperty(ref _sourcePoNo, value); }
        }

        private string _sourceSalesOrderNo;
        public string SourceSalesOrderNo
        {
            get { return _sourceSalesOrderNo; }
            set { SetProperty(ref _sourceSalesOrderNo, value); }
        }

        private int _pageIndex = 1;
        public int PageIndex
        {
            get { return _pageIndex; }
            set { SetProperty(ref _pageIndex, value); }
        }

        private int _pageSize = 50;
        public int PageSize
        {
            get { return _pageSize; }
            set { SetProperty(ref _pageSize, value); }
        }

        private int _totalCount;
        public int TotalCount
        {
            get { return _totalCount; }
            set { SetProperty(ref _totalCount, value); }
        }

        private string _statusMessage = "准备就绪";
        public string StatusMessage
        {
            get { return _statusMessage; }
            set { SetProperty(ref _statusMessage, value); }
        }
        public RelayCommand CreatDetailCommand { get; set; }
        public RelayCommand LoadCommand { get; private set; }
        public RelayCommand SaveCommand { get; private set; }
        public RelayCommand PostCommand { get; private set; }
        public RelayCommand ImportPurchaseOrderCommand { get; private set; }
        public RelayCommand ImportSalesOrderCommand { get; private set; }
        public RelayCommand AddNewCommand { get; private set; }
        public RelayCommand RemoveSelectedLineCommand { get; private set; }

        public RelayCommand<PageChangeRequest> PageChangeCommand
        {
            get
            {
                return new RelayCommand<PageChangeRequest>(async req =>
                {
                    PageIndex = req.NewIndex;
                    PageSize = req.NewSize;
                    await LoadAsync();
                }, req => IsNotBusy);
            }
        }

        private async Task LoadAsync()
        {
            if (_packingListApi == null)
            {
                StatusMessage = "PackingListApi 未注入";
                return;
            }

            using (BusyScope())
            {
                try
                {
                    var rst = await _packingListApi.GetHeadPagedListAsync(new PackingListSearchDto
                    {
                        //BatchKeywords =
                        PageIndex = PageIndex,
                        PageSize = PageSize
                    });

                    HeaderItems.Clear();
                    LineItems.Clear();

                    if (rst.Success && rst.Data != null)
                    {
                        var hds = new List<PackingListHeadModel>();
                        foreach (var item in rst.Data.Items)
                        {
                            var hd = new PackingListHeadModel();
                            hd.LoadFromDto(item);
                            hds.Add(hd);
                        }
                        HeaderItems.AddRangeFromModelList(hds);

                        TotalCount = rst.Data.TotalCount;
                        StatusMessage = "装箱单加载完成，共 " + TotalCount + " 条。";
                        //SelectedHeader = HeaderItems.Items[0];
                    }
                    else
                    {
                        StatusMessage = "装箱单加载失败：" + rst.Message;
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = "装箱单加载异常：" + ex.Message;
                }
            }
        }

        private async Task LoadDetailAsync()
        {
            LineItems.Clear();
            if (_packingListApi == null || SelectedHeader == null || SelectedHeader.Uid == Guid.Empty)
                return;

            try
            {
                var rst = await _packingListApi.GetDetailByUidAsync(SelectedHeader.Uid);
                if (rst.Success && rst.Data != null)
                {
                    var hds = new List<PackingListLineModel>();
                    foreach (var line in rst.Data.Lines)
                    {
                        var hd = new PackingListLineModel();
                        hd.LoadFromDto(line);
                    }
                    LineItems.AddRangeFromModelList(hds);
                }
                else
                {
                    StatusMessage = "装箱单明细加载失败：" + rst.Message;
                }
            }
            catch (Exception ex)
            {
                StatusMessage = "装箱单明细加载异常：" + ex.Message;
            }
        }

        private void AddNew()
        {
            var nw = new PackingListHeadModel();
            nw.MarkAsAdded();
            nw.ShipToName = "ceshi1";
            HeaderItems.AddModelToFirst(nw);
            //SelectedHeader = new PackingListHeadDto
            //{
            //    PackingDate = DateTime.Now,
            //    Status = "草稿"
            //};
            //LineItems.Clear();
            //StatusMessage = "已新建装箱单草稿。";
        }

        private async Task SaveAsync()
        {
            if (_packingListApi == null || SelectedHeader == null)
                return;

            if (LineItems.Items.Count == 0)
            {
                await _dialogService.ShowWarningAsync("请先从采购单或销售单引入明细。");
                return;
            }

            using (BusyScope())
            {
                try
                {
                    var rst = await _packingListApi.SaveDetailAsync(new PackingListDetailDto
                    {
                        Header = SelectedHeader.Adapt<PackingListHeadDto>(),
                        Lines = LineItems.GetAllItems().Adapt<List<PackingListLineDto>>()
                    });

                    if (rst.Success)
                    {
                        StatusMessage = "装箱单保存成功。";
                        await _dialogService.ShowInfoAsync("装箱单保存成功。");
                        await LoadAsync();
                    }
                    else
                    {
                        await _dialogService.ShowWarningAsync("保存失败：" + rst.Message);
                    }
                }
                catch (Exception ex)
                {
                    await _dialogService.ShowWarningAsync("保存异常：" + ex.Message);
                }
            }
        }

        private async Task PostAsync()
        {
            if (_packingListApi == null || SelectedHeader == null)
                return;

            if (SelectedHeader.Uid == Guid.Empty)
            {
                await SaveAsync();
                if (SelectedHeader == null || SelectedHeader.Uid == Guid.Empty)
                    return;
            }

            await PostCoreAsync(false);
        }

        private async Task PostCoreAsync(bool allowNegativeStock)
        {
            using (BusyScope())
            {
                try
                {
                    var rst = await _packingListApi.PostPackingListAsync(new PackingListPostRequestDto
                    {
                        PackingListId = SelectedHeader.Uid,
                        AllowNegativeStock = allowNegativeStock,
                        Operator = Environment.UserName
                    });

                    if (rst.Success && rst.Data != null && rst.Data.NeedNegativeStockConfirm)
                    {
                        var ok = await _dialogService.ShowConfirmAsync(rst.Data.WarningMessage, "库存不足");
                        if (ok)
                            await PostCoreAsync(true);
                        return;
                    }

                    if (rst.Success)
                    {
                        StatusMessage = "装箱单过账成功，入库单号：" + (rst.Data == null ? "" : rst.Data.ReceiveNo);
                        await _dialogService.ShowInfoAsync(StatusMessage);
                        await LoadAsync();
                    }
                    else
                    {
                        await _dialogService.ShowWarningAsync("过账失败：" + rst.Message);
                    }
                }
                catch (Exception ex)
                {
                    await _dialogService.ShowWarningAsync("过账异常：" + ex.Message);
                }
            }
        }

        private async Task ImportPurchaseOrderAsync()
        {
            if (_purchaseOrderApi == null || string.IsNullOrWhiteSpace(SourcePoNo))
                return;

            try
            {
                var rst = await _purchaseOrderApi.GetDetailByPoNoAsync(SourcePoNo.Trim());
                if (!rst.Success || rst.Data == null)
                {
                    await _dialogService.ShowWarningAsync("引入采购单失败：" + rst.Message);
                    return;
                }

                //EnsureHeader();
                //SelectedHeader.PoNo = rst.Data.Header.BoNo;
                //SelectedHeader.PurchaseOrderNo = rst.Data.Header.OrderNo;
                //SelectedHeader.SupplierCode = rst.Data.Header.SupplierCode;
                //SelectedHeader.SupplierName = rst.Data.Header.SupplierName;
                //SelectedHeader.SalesOrderNo = rst.Data.Header.SalesOrderNo;
                SourceSalesOrderNo = rst.Data.Header.SalesOrderNo;

                foreach (var line in rst.Data.Lines)
                {
                    LineItems.AddModelToFirst(new PackingListLineModel
                    {
                        Sku = line.Sku,
                        CustomerSku = line.CustomerSku,
                        Qty = Math.Max(line.ReadyQty, line.QTY - line.RecQty),

                        SalesOrderNo = rst.Data.Header.SalesOrderNo
                    });
                }

                RaisePropertyChanged(nameof(SelectedHeader));
                StatusMessage = "已从采购单引入 " + rst.Data.Lines.Count + " 行。";
            }
            catch (Exception ex)
            {
                await _dialogService.ShowWarningAsync("引入采购单异常：" + ex.Message);
            }
        }

        private async Task ImportSalesOrderAsync()
        {
            if (_salesOrderApi == null || string.IsNullOrWhiteSpace(SourceSalesOrderNo))
                return;

            try
            {
                var search = await _salesOrderApi.GetSalesOrderPagedListAsync(new SalesOrderSearchDto
                {
                    OrderNo = SourceSalesOrderNo.Trim(),
                    PageIndex = 1,
                    PageSize = 1
                });

                var header = search.Success && search.Data != null ? search.Data.Items.FirstOrDefault() : null;
                if (header == null)
                {
                    await _dialogService.ShowWarningAsync("未找到销售单：" + SourceSalesOrderNo);
                    return;
                }

                var detail = await _salesOrderApi.GetSalesOrderDetailAsync(header.Uid);
                if (!detail.Success || detail.Data == null)
                {
                    await _dialogService.ShowWarningAsync("引入销售单失败：" + detail.Message);
                    return;
                }

                EnsureHeader();


                foreach (var line in detail.Data.Lines)
                {

                    LineItems.AddModelToFirst(new PackingListLineModel
                    {
                        Sku = line.Sku,
                        CustomerSku = line.CustomerSku,
                        DescriptionEn = line.DescriptionEn,
                        SoLineId = line.Uid,
                        SalesOrderNo = detail.Data.Header.OrderNo
                    });
                }

                RaisePropertyChanged(nameof(SelectedHeader));
                StatusMessage = "已从销售单引入 " + detail.Data.Lines.Count + " 行。";
            }
            catch (Exception ex)
            {
                await _dialogService.ShowWarningAsync("引入销售单异常：" + ex.Message);
            }
        }

        private void RemoveSelectedLine()
        {
            if (SelectedLine == null) return;
            LineItems.Items.Remove(SelectedLine);
            SelectedLine = null;
        }

        private void EnsureHeader()
        {
            if (SelectedHeader == null)
                AddNew();
        }
    }
}
