using GalaSoft.MvvmLight.Command;
using Mapster;
using SwiftExport.Contracts.Apis.Orders;
using SwiftExport.Contracts.Apis.SupplierApis;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.Warehouses;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.UiInterface;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.View.FW.Mvvm;
using SwiftExport.View.FW.WarehouseViews.Models;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace SwiftExport.View.FW.WarehouseViews.ViewModels
{
    /// <summary>
    /// 入库单执行页面 ViewModel。
    /// 职责：
    /// 1) 按 PoNo 加载采购行；
    /// 2) 输入入库数量并提交 ExecutePurchaseReceiveAsync；
    /// 3) 成功后刷新明细。
    /// </summary>
    public class ReceiptDetailViewModel : ViewModelBaseEx
    {
        private readonly IPurchaseOrderApi _purchaseOrderApi;
        private readonly IReceiveApi _receiveApi;
        private readonly ISupplierApi _supplierApi;
        private readonly IDialogService _dialog;

        public ObservableCollection<SupplierDto> Suppliers { get; } = new ObservableCollection<SupplierDto>();
        public ObservableCollection<string> MaterialTypes { get; } = new ObservableCollection<string>();

        public ReceiveLineList LineTable { get; } = new ReceiveLineList();
        public ReceiveHeadModel ReceiveHead { get; }
        public ReceiveDetailModel ReceiveDetail { get; set; }

        private string _materialType = MaterialTypesEnum.客供件;
        public string MaterialType
        {
            get => _materialType;
            set => SetProperty(ref _materialType, value);
        }

        private string _statusMessage = "请选择供应商与物料类型后点击【加载】";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public RelayCommand SaveDetailCommand { get; }

        public ReceiptDetailViewModel(IPurchaseOrderApi purchaseOrderApi, IReceiveApi receiveApi, ISupplierApi supplierApi, IDialogService dialog)
        {
            _purchaseOrderApi = purchaseOrderApi;
            _receiveApi = receiveApi;
            _supplierApi = supplierApi;
            _dialog = dialog;

            foreach (var item in MaterialTypesEnum.MaterialTypes)
                MaterialTypes.Add(item);

            SaveDetailCommand = new RelayCommand(async () => await SaveDetailAsync());

            _ = 加载依赖数据();
        }


        private async Task 加载依赖数据()
        {
            _ = LoadSuppliersAsync();
        }

        private async Task LoadSuppliersAsync()
        {
            try
            {
                var rst = await _supplierApi.GetAllAsync();
                if (!rst.Success || rst.Data == null) return;

                Suppliers.Clear();
                foreach (var s in rst.Data.OrderBy(x => x.SupplierCode))
                    Suppliers.Add(s);
            }
            catch
            {
                // 中文注释：供应商加载失败不阻断页面，用户可手输编码。
            }
        }

        public bool CanSaveDetil()
        {
          return !ReceiveDetail.ReceiveHeadModel.IsPosted;
        }
        private async Task SaveDetailAsync()
        {
            var selected = LineTable.Items.Where(x => x.IsChecked).ToList();
            if (selected.Count == 0)
            {
                await _dialog.ShowInfoAsync("请先勾选要入库的行。");
                return;
            }

            var invalid = selected.FirstOrDefault(x => !x.IsValidReceiveQty);
            if (invalid != null)
            {
                await _dialog.ShowWarningAsync($"SKU={invalid.Sku} 的入库数量无效，必须 > 0 且 <= 可入库数量。", "校验失败");
                return;
            }


            var relatedPoNos = selected
                .Select(x => x.PoNo)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var changedSet = ReceiveDetail.ReceiveLineList.GetChangeSet();
            var req = new SaveDocumentRequestBase<ReceiveHeadDto, ReceiveLineDto>
            {
                Header = ReceiveDetail.ReceiveHeadModel.Adapt<ReceiveHeadDto>(),
                AddLines = changedSet.AddList.Select(x => x.Adapt<ReceiveLineDto>()).ToList(),
                UpdateLines = changedSet.UpdateList.Select(x => x.Adapt<ReceiveLineDto>()).ToList(),                
                DeleteLineIds = changedSet.DeleteIdList
            };

            using (BusyScope())
            {
                try
                {
                    StatusMessage = "正在保存单据...";
                    var rst = await _receiveApi.SaveDetailAsync(req);
                    if (rst.Success)
                    {
                        StatusMessage = "保存成功，单号：" + rst.Data;
                        await _dialog.ShowInfoAsync(StatusMessage);
                    }
                    else
                    {
                        StatusMessage = "保存失败：" + rst.Message;
                        await _dialog.ShowWarningAsync(StatusMessage);
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = "异常：" + ex.Message;
                    await _dialog.ShowErrorAsync(StatusMessage);
                }
            }
        }
    }
}
