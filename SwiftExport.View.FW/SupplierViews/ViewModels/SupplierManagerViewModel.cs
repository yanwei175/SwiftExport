using GalaSoft.MvvmLight.Command;
using SwiftExport.Contracts.Apis.SupplierApis;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.UiInterface;
using SwiftExport.View.FW.Mvvm;
using SwiftExport.View.FW.SupplierViews.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SwiftExport.View.FW.SupplierViews.ViewModels
{
    /// <summary>
    /// 供应商管理页签枚举。
    /// </summary>
    public enum SupplierManagerTabs
    {
        SupplierList = 0,
        SupplierProducts = 1
    }

    public class SupplierManagerViewModel : BaseSmallDataManagerViewModel<SupplierModel, SupplierDto>
    {
        private readonly ISupplierApi _myApi;
        private readonly IDialogService _msg;

        // 注入泛型 API，并把路由丢给基类！
        public SupplierManagerViewModel(ISupplierApi api, IDialogService dialogService) : base(api, dialogService)
        {
            _msg = dialogService;
            _myApi = api;

            AddSupplierCommand = AddNewRowCommand;
            BatchSaveSupplierAsyncCommand = BatchSaveCommand;
            LoadSupplierAsyncCommand = LoadCommand;
            InvertCheckSupplierCommand = new RelayCommand(() =>
            {
                foreach (var item in AllItems)
                {
                    item.IsChecked = !item.IsChecked;
                }
                EvaluateChanges();
            });
        }

        private SupplierManagerTabs _activeTab;
        /// <summary>
        /// 当前激活页签。
        /// </summary>
        public SupplierManagerTabs ActiveTab
        {
            get => _activeTab;
            set => SetProperty(ref _activeTab, value);
        }

        private SupplierModel _selectedSupplier;
        /// <summary>
        /// 当前选中供应商。
        /// </summary>
        public SupplierModel SelectedSupplier
        {
            get => _selectedSupplier;
            set => SetProperty(ref _selectedSupplier, value);
        }

        /// <summary>
        /// 供应商搜索文本（业务语义命名）。
        /// </summary>
        public string SearchSupplierText
        {
            get => SearchText;
            set => SearchText = value;
        }

        /// <summary>
        /// 供应商总数（用于页导航展示）。
        /// </summary>
        public int TotalSupplierCount => AllItems == null ? 0 : AllItems.Count;

        /// <summary>
        /// 供应商分页页码（当前小数据管理不分页，固定返回1以兼容UI绑定）。
        /// </summary>
        public int PageSupplierIndex => 1;

        /// <summary>
        /// 供应商分页页大小（当前小数据管理不分页，返回当前条数）。
        /// </summary>
        public int PageSupplierSize => TotalSupplierCount <= 0 ? 1 : TotalSupplierCount;

        // 业务命名命令别名（便于XAML可读性）
        public RelayCommand AddSupplierCommand { get; }
        public RelayCommand BatchSaveSupplierAsyncCommand { get; }
        public RelayCommand LoadSupplierAsyncCommand { get; }
        public RelayCommand InvertCheckSupplierCommand { get; }

        //实现一下怎么搜索供应商
        protected override bool FilterItem(SupplierModel item, string searchText)
        {
            return (item.SupplierCode?.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (item.Name?.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (item.DocRelateNo?.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (item.ContactPerson?.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        protected override async Task<bool> ValidateBeforeSaveAsync(List<SupplierModel> dirtyModels)
        {
            // 1. 必填项空值拦截 (这是替代了以前前端红框的防线)
            var emptyItems = dirtyModels.Where(x => !x.IsValid).ToList();
            if (emptyItems.Any())
            {
                await _msg.ShowWarningAsync("存在未填写完整的行！\n\n【供应商】和【供应商名称】和【单据关联号】为必填项，请补充完整后再保存。");
                return false;
            }
            // 我们把当前 DataGrid 里所有没有被删除的数据拿出来分组
            var activeItems = AllItems.Where(x => x.State != ModelState.Deleted).ToList();

            // ★ 架构师防呆升级：统一转大写 + 去除首尾空格 + 防 Null
            var duplicates = activeItems
                .GroupBy(x => new
                {
                    CustomerCode = x.SupplierCode?.Trim().ToUpper() ?? ""
                })
                .Where(g => g.Count() > 1)
                .ToList(); // 这里不要直接 Select Key，我们把整组数据留下来

            if (duplicates.Any())
            {
                // ★ 体验细节：拿到重复组里的“第一行真实数据”，这样弹窗报错时显示的是用户原来输入的真实大小写
                var firstDupItem = duplicates.First().First();

                await _msg.ShowWarningAsync($"列表中存在重复的配置！\n\n" +
                                  $"供应商代码：{firstDupItem.SupplierCode}\n" +
                                  $"请修改或删除重复行后再保存。");

                // 返回 false 告诉基类：拦截失败！不要发请求！
                return false;
            }

            // 返回 true 告诉基类：校验完美通过，放行！
            return true;
        }

        protected override Task AfterSaveSuccessAsync()
        {
            RaisePropertyChanged(nameof(TotalSupplierCount));
            RaisePropertyChanged(nameof(PageSupplierIndex));
            RaisePropertyChanged(nameof(PageSupplierSize));
            return base.AfterSaveSuccessAsync();
        }
    }
}