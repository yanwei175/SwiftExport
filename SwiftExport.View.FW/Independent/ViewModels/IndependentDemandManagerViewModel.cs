using GalaSoft.MvvmLight.Command;
using SwiftExport.Contracts.Apis.Orders;
using SwiftExport.Contracts.DTOs.Independent;
using SwiftExport.Contracts.Models;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.UiInterface;
using SwiftExport.View.FW.Independent.Models;
using SwiftExport.View.FW.Mvvm;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

namespace SwiftExport.View.FW.Independent.ViewModels
{
    public class IndependentDemandManagerViewModel : ViewModelBaseEx
    {
        private readonly IDialogService _msg;
        private readonly IIndependentDemandApi _independApi;

        public IndependentDemandManagerViewModel(IDialogService dialogService, IIndependentDemandApi independentDemandApi)
        {
            _msg = dialogService;
            _independApi = independentDemandApi;
            HeadTable = new IndependentDemandListModel();
            InitializeCommands();
            _ = LoadDataAsync();

        }
        public IndependentDemandListModel HeadTable { get; set; }
        private IndependentDemandModel _selectedHeadModel;
        public IndependentDemandModel SelectedHeadModel
        {
            get => _selectedHeadModel;
            set => SetProperty(ref _selectedHeadModel, value);
        }

        private async Task LoadDataAsync(IndependSearchRequest req = null)
        {
            StatusMessage = "正在加载数据...";
            try
            {
                var result = await _independApi.GetPagedAsync(req);
                HeadTable.ReLoadRangeFromDtoList(result.Data.Items);
                TotalCount = result.Data.TotalCount;
                StatusMessage = $"共 {result.Data.TotalCount} 条记录";
            }
            catch (Exception ex)
            {
                StatusMessage = "加载数据失败: " + ex.Message;
            }
        }



        #region  命令

        private void InitializeCommands()
        {
            CreatIndependComand = new RelayCommand(CreateIndepend);
            SaveCommand = new RelayCommand(async () => await SaveAsync(), CanSave);
            RegisterBusyNotifyCommand(SaveCommand);
            HeadTable.RegisterDirtyNotifyCommand(SaveCommand);

            ConfirmCommand = new RelayCommand(async () => await ConfirmAsync(), CanConfirm);
            RegisterBusyNotifyCommand(ConfirmCommand);
            HeadTable.RegisterCheckedNotifyCommand(ConfirmCommand);
            HeadTable.RegisterDirtyNotifyCommand(ConfirmCommand);

            CancelConfirmCommand = new RelayCommand(async () => await CancelConfirmAsync(), CanCancelConfirm);
            RegisterBusyNotifyCommand(CancelConfirmCommand);
            HeadTable.RegisterCheckedNotifyCommand(CancelConfirmCommand);
            HeadTable.RegisterDirtyNotifyCommand(CancelConfirmCommand);

            DecomposeToDemandCommand = new RelayCommand(DecomposeToDemand, CanDecomposeToDemand);
            RegisterBusyNotifyCommand(DecomposeToDemandCommand);
            HeadTable.RegisterCheckedNotifyCommand(DecomposeToDemandCommand);
            HeadTable.RegisterDirtyNotifyCommand(DecomposeToDemandCommand);

            SearchCommand = new RelayCommand(async () => await LoadDataAsync(BuildSearchRequest()), IsNotBusy);
            RegisterBusyNotifyCommand(SearchCommand);
            ResetFilterCommand = new RelayCommand(ResetFilter, IsNotBusy);
            RegisterBusyNotifyCommand(ResetFilterCommand);
            ImportFromSalesOrderCommand = new RelayCommand(ImportFromSalesOrder, IsNotBusy);
            RegisterBusyNotifyCommand(ImportFromSalesOrderCommand);
            ImportFromAfterSalesOrderCommand = new RelayCommand(ImportFromAfterSalesOrder, IsNotBusy);
            RegisterBusyNotifyCommand(ImportFromAfterSalesOrderCommand);

        }
        public RelayCommand CreatIndependComand { get; private set; }
        public RelayCommand SaveCommand { get; private set; }
        public RelayCommand ConfirmCommand { get; private set; }
        public RelayCommand CancelConfirmCommand { get; private set; }
        public RelayCommand DecomposeToDemandCommand { get; private set; }

        public RelayCommand SearchCommand { get; private set; }
        public RelayCommand ResetFilterCommand { get; private set; }
        public RelayCommand ImportFromSalesOrderCommand { get; private set; }
        public RelayCommand ImportFromAfterSalesOrderCommand { get; private set; }
        private void CreateIndepend()
        {
            var md = new IndependentDemandModel
            {
                Uid = Guid.Empty,
                SourceNo = "SO123456",
                SourceType = "SalesOrder",
                Sku = "PROD001",
                SourceSku = "CUST001"
            };
            md.MarkAsAdded();
            HeadTable.AddModelToFirst(md);
        }

        private bool CanSave()
        {
            return IsNotBusy && HeadTable.IsDirty;
        }
        private async Task SaveAsync()
        {
            await _independApi.BatchSaveAsync(new IndependentBatchSaveRequest
            {
            });
        }
        private bool CanConfirm()
        {
            return HeadTable.CheckedCount > 0 && IsNotBusy && !HeadTable.IsDirty && HeadTable.Items.Any(x => x.ConfirmationStatus != ConfirmationStatusEnum.已确认);
        }
        private async Task ConfirmAsync()
        {

        }

        private bool CanCancelConfirm()
        {
            return HeadTable.CheckedCount > 0 && IsNotBusy && !HeadTable.IsDirty && HeadTable.Items.Any(x => x.ConfirmationStatus == ConfirmationStatusEnum.已确认);
        }
        private async Task CancelConfirmAsync()
        {

        }

        private bool CanDecomposeToDemand()
        {
            return SelectedHeadModel != null && HeadTable.CheckedCount > 0 && IsNotBusy && !HeadTable.IsDirty;
        }
        private void DecomposeToDemand()
        {
            if (SelectedHeadModel == null)
            {
                _msg.ShowWarningAsync("请先选择一行数据");
                return;
            }

        }

        private void ResetFilter()
        {
            FilterSourceNo = null;
            FilterText = null;
            FilterIsDecomposed = false;
            FilterFromDate = null;
            FilterToDate = null;
        }

        private void ImportFromSalesOrder()
        { }
        private void ImportFromAfterSalesOrder()
        { }


        #endregion

        #region  搜索
        private IndependSearchRequest BuildSearchRequest()
        {
            return new IndependSearchRequest
            {
                SourceNo = FilterSourceNo,
                Keyword = FilterText,
                IsDecomposed = FilterIsDecomposed,
                StartDate = FilterFromDate,
                EndDate = FilterToDate,
                PageIndex = PageIndex,
                PageSize = PageSize
            };
        }

        private string _filterSourceNo;
        public string FilterSourceNo
        {
            get => _filterSourceNo;
            set => SetProperty(ref _filterSourceNo, value);
        }

        private string _filterText;
        public string FilterText
        {
            get => _filterText;
            set
            {
                if (SetProperty(ref _filterText, value))
                    _ = LoadDataAsync(BuildSearchRequest()); // 过滤条件变化 → 加载数据
            }
        }

        private bool _filterIsDecomposed;
        public bool FilterIsDecomposed
        {
            get => _filterIsDecomposed;
            set => SetProperty(ref _filterIsDecomposed, value);
        }
        private DateTime? _filterFromDate;
        public DateTime? FilterFromDate
        {
            get => _filterFromDate;
            set => SetProperty(ref _filterFromDate, value);
        }
        private DateTime? _filterToDate;
        public DateTime? FilterToDate
        {
            get => _filterToDate;
            set => SetProperty(ref _filterToDate, value);
        }


        #endregion


        #region 分页与状态栏


        private int _pageIndex = 1;
        public int PageIndex
        {
            get => _pageIndex;
            set
            {
                if (_pageIndex == value) return;
                if (SetProperty(ref _pageIndex, value))
                    _ = LoadDataAsync(); // 页码变化 → 加载数据
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
                    // 换页大小时回第一页
                    if (PageIndex == 1) _ = LoadDataAsync();
                    else PageIndex = 1;
                }
            }
        }

        private int _totalCount;
        public int TotalCount
        {
            get => _totalCount;
            set => SetProperty(ref _totalCount, value);
        }

        private string _statusMessage = "准备就绪";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }
        public ICommand PageChangeCommand => new RelayCommand<PageChangeRequest>(async req =>
        {
            // ★ 拦截点：检查是否有未保存数据
            if (HeadTable.IsDirty)
            {
                var ok = await _msg.ShowConfirmAsync("当前有未保存修改，翻页将丢失，是否继续？", "提示");
                if (!ok) return; // 不执行 → UI 因 Mode=OneWay 自动保持原值
            }

            // 正常翻页：更新属性 → 触发 LoadProductAsync
            PageSize = req.NewSize;
            PageIndex = req.NewIndex;
        });
        #endregion

    }
}
