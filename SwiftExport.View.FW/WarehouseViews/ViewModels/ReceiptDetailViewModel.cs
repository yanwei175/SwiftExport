using GalaSoft.MvvmLight.Command;
using Mapster;
using SwiftExport.Contracts.Apis.Orders;
using SwiftExport.Contracts.Apis.SupplierApis;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.Warehouses;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.UiInterface;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.View.FW.DI;
using SwiftExport.View.FW.Http;
using SwiftExport.View.FW.Mvvm;
using SwiftExport.View.FW.WarehouseViews.Models;
using SwiftExport.View.FW.WarehouseViews.Views;
using System;
using System.Collections.Generic;
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

        /// <summary> 弹窗后的结果</summary>
        public int ResultData { get; set; }

        /// <summary> 关闭弹窗的委托 </summary>
        public event EventHandler<bool> RequestClose;

        public ObservableCollection<SupplierDto> Suppliers { get; } = new ObservableCollection<SupplierDto>();
        public ObservableCollection<string> MaterialTypes { get; } = new ObservableCollection<string>();

        public ReceiveLineList LineTable { get; } = new ReceiveLineList();
        public ReceiveHeadModel ReceiveHead { get; set; } = new ReceiveHeadModel();

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
        public RelayCommand GetAwaitingInboundCommand { get; }
        public RelayCommand DeleteLineCommand { get; }
        public RelayCommand SaveDetailCommand { get; }
        public RelayCommand CloseWindowCommand { get; }

        public ReceiptDetailViewModel(IPurchaseOrderApi purchaseOrderApi, IReceiveApi receiveApi, ISupplierApi supplierApi, IDialogService dialog)
        {
            _purchaseOrderApi = purchaseOrderApi;
            _receiveApi = receiveApi;
            _supplierApi = supplierApi;
            _dialog = dialog;

            foreach (var item in MaterialTypesEnum.MaterialTypes)
                MaterialTypes.Add(item);
            GetAwaitingInboundCommand = new RelayCommand(GetAwaitingInbound);
            DeleteLineCommand = new RelayCommand(DeleteLine);
            SaveDetailCommand = new RelayCommand(async () => await SaveDetailAsync(), CanSaveDetil);
            CloseWindowCommand = new RelayCommand(CloseWindow, CanCloseWindow);


            _ = 加载依赖数据();
        }

        /// <summary>
        /// 新增模式初始化。
        /// </summary>
        public void InitForCreate()
        {
            ReceiveHead = new ReceiveHeadModel();
            ReceiveHead.ReceiveDate = DateTime.Now;
            ReceiveHead.Handler = AuthTokenStore.CurrentUserName ?? AuthTokenStore.CurrentNickName;
            LineTable.Clear();
            StatusMessage = "新建入库单";
        }

        /// <summary>
        /// 编辑模式初始化：带入头信息并加载行明细。
        /// </summary>
        public async Task InitForEditAsync(ReceiveHeadModel head)
        {
            if (head == null) return;

            ReceiveHead = new ReceiveHeadModel
            {
                Id = head.Id,
                ReceiveNo = head.ReceiveNo,
                SupplierCode = head.SupplierCode,
                ReceiveDate = head.ReceiveDate,
                Handler = head.Handler,
                Remark = head.Remark,
                RelatedPoNos = head.RelatedPoNos,
                IsPosted = head.IsPosted,
                Version = head.Version
            };

            LineTable.Clear();
            var rst = await _receiveApi.GetLinesByHeadIdAsync(head.Id);
            if (rst.Success && rst.Data != null)
            {
                LineTable.ReLoadRangeFromDtoList(rst.Data.ToList());
                StatusMessage = $"编辑入库单：{head.ReceiveNo}";
            }
            else
            {
                StatusMessage = "加载入库明细失败：" + rst.Message;
            }
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

        private void GetAwaitingInbound()
        {
            try
            {

                var win = IocProvider.GetService<AwatingInboundView>();
                var vm = win.DataContext as AwatingInboundViewModel;
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

                    if (finalResult == null || finalResult.Count <= 0) { return; }

                    var r = _receiveApi.GetLinesByHeadIdAsync(ReceiveHead.Id).Result;
                    if (r == null) return;

                    List<ReceiveLineModel> f = new List<ReceiveLineModel>();
                    foreach (var line in finalResult)
                    {
                        var model = new ReceiveLineModel();
                        model.LoadFromDto(line);
                        f.Add(model);
                    }

                    LineTable.AddRangeFromModelList(f);
                }

            }
            catch
            {

            }

        }
        private void DeleteLine()
        {
            var selected = LineTable.Items.Where(x => x.IsChecked).ToList();
            if (selected.Count == 0)
            {
                _dialog.ShowInfoAsync("请先勾选要删除的行。");
                return;
            }
            foreach (var item in selected)
                LineTable.Remove(item);
        }

        public bool CanSaveDetil()
        {
            return !ReceiveHead.IsPosted;
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
            var changedSet = LineTable.GetChangeSet();
            var req = new SaveDocumentRequestBase<ReceiveHeadDto, ReceiveLineDto>
            {
                Header = ReceiveHead.Adapt<ReceiveHeadDto>(),
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
                        StatusMessage = "保存成功";

                        await _dialog.ShowInfoAsync(StatusMessage);

                        ResultData = rst.Data; //将新单号Id传回调用者
                        RequestClose?.Invoke(this, true);
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
        public bool CanCloseWindow()
        {
            return !ReceiveHead.IsDirty;
        }
        private void CloseWindow()
        {
            ResultData = -1;
            RequestClose?.Invoke(this, true);
        }
    }
}
