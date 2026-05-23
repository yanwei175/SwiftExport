using GalaSoft.MvvmLight.Command;
using SwiftExport.Contracts.Apis.Orders;
using SwiftExport.Contracts.Apis.SystemApis;
using SwiftExport.Contracts.DTOs.PackingLists;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.UiInterface;
using SwiftExport.View.FW.DI;
using SwiftExport.View.FW.Mvvm;
using SwiftExport.View.FW.PackingListViews.Models;
using SwiftExport.View.FW.PackingListViews.Views;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SwiftExport.View.FW.PackingListViews.ViewModels
{
    public class PackingListDetailViewModel : ViewModelBaseEx
    {
        private readonly IPackingListApi _pkApi;
        private readonly IDocumentNoApi _docNoApi;
        private readonly IDialogService _dialog;
        public PackingListDetailViewModel(IPackingListApi packingListApi, IDocumentNoApi documentNoApi, IDialogService dialogService)
        {
            _pkApi = packingListApi;
            _docNoApi = documentNoApi;
            _dialog = dialogService;
            OrderHead = new PackingListHeadModel();
            LineTable = new PackingListLineList();
            LineTable.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(LineTable.IsDirty))
                {
                    SaveDetailCommand.RaiseCanExecuteChanged();
                }
            };
            InitCommands();
        }
        public PackingListHeadModel OrderHead { get; private set; }
        public PackingListLineList LineTable { get; private set; }

        public void InitFromCreate()
        {
            OrderHead = new PackingListHeadModel();
            LineTable.Clear();
        }
        public async Task InitFromEdit(int headId)
        {
            var rst = await _pkApi.GetDetailByIdAsync(headId);
            if (rst != null)
            {
                using (BusyScope())
                {
                    OrderHead.LoadFromDto<PackingListHeadDto>(rst.Data.Header);
                    LineTable.ReLoadRangeFromDtoList(rst.Data.Lines);

                }
            }
        }

        #region 命令
        private void InitCommands()
        {
            ItemCheckedChangedCommand = new RelayCommand(ItemCheckedChanged);
            GetAwatingOutboundCommand = new RelayCommand(GetAwatingOutbound);

            DeleteLineCommand = new RelayCommand<PackingListLineModel>(async (line) => await DeleteLine(line));

            RefreshLineTableCommand = new RelayCommand(RefreshLineTable);
            RegisterBusyNotifyCommand(RefreshLineTableCommand);


            SaveDetailCommand = new RelayCommand(async () => await SaveDetailAsync(), () => CanSave);
            RegisterBusyNotifyCommand(SaveDetailCommand);
            LineTable.RegisterDirtyNotifyCommand(SaveDetailCommand);


            MatchSalesLineCommand = new RelayCommand(async () => await MatchSalesLineAsync());
            RegisterBusyNotifyCommand(MatchSalesLineCommand);

            InvertCheckCommand = new RelayCommand(InvertCheck, () => LineTable.Items.Count > 0);
            LineTable.RegisterCheckedNotifyCommand(InvertCheckCommand);

            GetNewNoCommand = new RelayCommand(async () => await GetNewNoAsync());
            CostomerSelectorCommand = new RelayCommand(ShowCustomerSelector);

            CancelCommand = new RelayCommand(Cancel);
        }
        public RelayCommand ItemCheckedChangedCommand { get; private set; }
        public RelayCommand GetAwatingOutboundCommand { get; private set; }
        public RelayCommand<PackingListLineModel> DeleteLineCommand { get; private set; }
        public RelayCommand RefreshLineTableCommand { get; private set; }
        public bool CanSave =>
                            OrderHead != null &&
                            OrderHead.IsValid &&
                            LineTable.IsAllValid &&
                            (OrderHead.State != ModelState.Unchanged || LineTable.IsDirty);
        public RelayCommand SaveDetailCommand { get; private set; }
        public RelayCommand CancelCommand { get; private set; }
        public RelayCommand InvertCheckCommand { get; private set; }
        /// <summary>
        /// 根据数据匹配销售单行
        /// </summary>
        public RelayCommand MatchSalesLineCommand { get; private set; }
        public RelayCommand GetNewNoCommand { get; private set; }
        public RelayCommand CostomerSelectorCommand { get; private set; }

        #endregion

        #region 命令方法
        private async Task GetNewNoAsync()
        {
            var rst = await _docNoApi.GetNextAsync(new DocumentNoRequestDto { DocType = DocumentTypeStaticsEnum.PackingList });
            DispatcherProvider.Invoke(() =>
             {
                 if (rst.Success)
                 {
                     OrderHead.PackingListNo = rst.Data.DocumentNo;
                 }
                 else
                 {
                     //获取单号编号失败,手动建立一个精确到毫秒的单据编号，避免重复
                     OrderHead.PackingListNo = "PL" + DateTime.Now.ToString("yyyyMMddHHmmssfff");
                 }
             });
        }

        private void ShowCustomerSelector()
        {

        }

        private void InvertCheck() => LineTable.InvertCheckAll();
        private void GetAwatingOutbound()
        {
            try
            {

                var win = IocProvider.GetService<UnshippedView>();
                var vm = win.DataContext as UnshippedViewModel;
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

                    //var r = _receiveApi.GetLinesByHeadIdAsync(ReceiveHead.Id).Result;
                    //if (r == null) return;

                    List<PackingListLineModel> f = new List<PackingListLineModel>();
                    foreach (var line in finalResult)
                    {
                        var model = new PackingListLineModel();
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
        private void ItemCheckedChanged() { }
        private async void RefreshLineTable()
        {
            if (OrderHead.Id == 0) return;
            var rst = await _pkApi.GetLinesByHeadIdAsync(OrderHead.Id);
            if (rst != null)
            {
                using (BusyScope())
                {
                    LineTable.ReLoadRangeFromDtoList(rst.Data);
                }
            }
        }

        private async Task DeleteLine(PackingListLineModel line)
        {
            //await _dialog.ShowConfirmAsync("测试测");
            if (line == null) return;
            LineTable.Remove(line);
        }
        private async Task SaveDetailAsync()
        {
            await _dialog.ShowConfirmAsync("测试测");
        }
        private void Cancel()
        {

        }
        private async Task MatchSalesLineAsync()
        {

        }

        #endregion



        #region 属性

        private PackingListLineModel _selectedLine;
        public PackingListLineModel SelectedLine
        {
            get => _selectedLine;
            set => SetProperty(ref _selectedLine, value);
        }
        private string _statusMessage;
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        #endregion




    }
}
