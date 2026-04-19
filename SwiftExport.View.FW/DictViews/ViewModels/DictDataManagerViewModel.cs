using GalaSoft.MvvmLight.Command;
using Mapster;
using SwiftExport.Contracts.Apis.SystemApis;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.UiInterface;
using SwiftExport.View.FW.DictViews.Models;
using SwiftExport.View.FW.Mvvm;
using SwiftExport.View.FW.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;

namespace SwiftExport.View.FW.DictViews.ViewModels
{
    // 中文注释：字典值管理 ViewModel（独立窗体）
    public class DictDataManagerViewModel : BaseSmallDataManagerViewModel<DictDataModel, DictDataDto>
    {
        private readonly ISysDictTypeApi _dictTypeApi;
        private readonly ISysDictDataApi _dictDataApi;
        private readonly IDialogService _msg;
        private readonly IDictCacheService _dictCacheService;

        public ObservableCollection<DictTypeModel> DictTypes { get; set; }
        public ICollectionView DictTypesView { get; private set; }

        private bool _isRevertingSelection = false;

        private DictTypeModel _selectedDictType;
        public DictTypeModel SelectedDictType
        {
            get => _selectedDictType;
            set
            {
                if (_selectedDictType == value) return;

                if (_isRevertingSelection)
                {
                    SetProperty(ref _selectedDictType, value);
                    return;
                }

                var oldValue = _selectedDictType;

                if (SetProperty(ref _selectedDictType, value))
                {
                    if (HasChanges && oldValue != null)
                    {
                        _ = CheckAndChangeAsync(oldValue, value);
                    }
                    else
                    {
                        _ = LoadDataAsync();
                    }
                }
            }
        }

        private async Task CheckAndChangeAsync(DictTypeModel oldValue, DictTypeModel newValue)
        {
            bool isConfirm = await _msg.ShowConfirmAsync("当前字典有未保存的修改，切换分类将丢失这些数据，是否继续？", "警告");
            if (!isConfirm)
            {
                _isRevertingSelection = true;
                SelectedDictType = oldValue;
                _isRevertingSelection = false;
            }
            else
            {
                _ = LoadDataAsync();
            }
        }

        private string _searchTypeText;
        public string SearchTypeText
        {
            get => _searchTypeText;
            set
            {
                if (SetProperty(ref _searchTypeText, value))
                {
                    DictTypesView.Filter = obj =>
                    {
                        if (string.IsNullOrWhiteSpace(value)) return true;
                        var m = (DictTypeModel)obj;
                        return (m.DictName?.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0) ||
                               (m.DictCode?.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0);
                    };
                    DictTypesView.Refresh();
                }
            }
        }

        public RelayCommand AddTypeCommand { get; }

        public DictDataManagerViewModel(ISysDictTypeApi dictTypeApi, ISysDictDataApi dictDataApi, IDialogService dialogService, IDictCacheService dictCacheService)
            : base(dictDataApi, dialogService)
        {
            _msg = dialogService;
            _dictTypeApi = dictTypeApi;
            _dictDataApi = dictDataApi;
            _dictCacheService = dictCacheService;

            DictTypes = new ObservableCollection<DictTypeModel>();
            DictTypesView = CollectionViewSource.GetDefaultView(DictTypes);
            AddTypeCommand = new RelayCommand(async () => await AddTypeAsync());
            _ = LoadDictTypesAsync();
        }

        // 中文注释：加载字典类型列表
        private async Task LoadDictTypesAsync()
        {
            try
            {
                var rst = await _dictTypeApi.GetAllAsync();
                if (rst.Success && rst.Data != null)
                {
                    _dispatcher.Invoke(() =>
                    {
                        DictTypes.Clear();
                        var models = rst.Data.Adapt<List<DictTypeModel>>();
                        foreach (var m in models) DictTypes.Add(m);

                        SelectedDictType = DictTypes.FirstOrDefault();
                    });
                }
            }
            catch (Exception ex)
            {
                StatusMessage = "加载字典分类失败: " + ex.Message;
            }
        }

        // 中文注释：根据选中的类型加载字典值
        protected override async Task LoadDataAsync()
        {
            if (SelectedDictType == null)
            {
                _dispatcher.Invoke(() =>
                {
                    AllItems.Clear();
                    HasChanges = false;
                    StatusMessage = "请先选择一个字典分类。";
                });
                return;
            }

            StatusMessage = $"正在加载【{SelectedDictType.DictName}】的字典值...";
            try
            {
                var rst = await _dictDataApi.GetByCodeAsync(SelectedDictType.DictCode);

                _dispatcher.Invoke(() =>
                {
                    if (rst.Success && rst.Data != null)
                    {
                        AllItems.Clear();
                        var models = rst.Data.Adapt<List<DictDataModel>>();
                        foreach (var model in models)
                        {
                            model.State = ModelState.Unchanged;
                            AllItems.Add(model);
                        }
                        HasChanges = false;
                        DebounceFilter(SearchText);
                        StatusMessage = $"加载成功，共 {AllItems.Count} 个字典值。";
                    }
                    else
                    {
                        AllItems.Clear();
                        StatusMessage = "加载字典值失败: " + rst.Message;
                    }
                });
            }
            catch (Exception ex)
            {
                StatusMessage = "加载异常: " + ex.Message;
            }
        }

        // 中文注释：在字典值窗体内支持快速新增字典类型
        private async Task AddTypeAsync()
        {
            var newName = await _msg.ShowInputAsync("新建字典类型", "请输入字典类型名称 (如：客户类型)");
            if (string.IsNullOrWhiteSpace(newName)) return;

            var newCode = await _msg.ShowInputAsync("新建字典类型", "请输入字典类型唯一编码 (如：CustomerType)");
            if (string.IsNullOrWhiteSpace(newCode)) return;

            var newTypeDto = new DictTypeDto
            {
                DictCode = newCode.Trim(),
                DictName = newName.Trim(),
                IsSystem = false
            };

            StatusMessage = "正在创建字典类型...";
            var rst = await _dictTypeApi.CreateAsync(newTypeDto);

            if (rst.Success)
            {
                await _dictCacheService.RefreshAllAsync();
                await _msg.ShowInfoAsync("新建字典类型成功！");
                await LoadDictTypesAsync();
            }
            else
            {
                await _msg.ShowErrorAsync("新建字典类型失败：" + rst.Message);
            }
        }

        protected override void AddNewRow()
        {
            if (SelectedDictType == null)
            {
                _ = _msg.ShowWarningAsync("请先选择一个字典分类！");
                return;
            }

            base.AddNewRow();

            var newlyAdded = AllItems.First();
            newlyAdded.DictCode = SelectedDictType.DictCode;
            newlyAdded.IsSystem = false;
            newlyAdded.SortOrder = AllItems.Count > 1 ? AllItems.Max(x => x.SortOrder) + 10 : 10;
        }

        protected override bool FilterItem(DictDataModel item, string searchText)
        {
            return (item.DictLabel?.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (item.DictValue?.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        protected override async Task<bool> ValidateBeforeSaveAsync(List<DictDataModel> dirtyModels)
        {
            if (dirtyModels.Any(x => !x.IsValid))
            {
                await _msg.ShowWarningAsync("存在未填写完整的字典值！\n\n【显示名称】和【真实键值】为必填项。");
                return false;
            }

            var activeItems = AllItems.Where(x => x.State != ModelState.Deleted).ToList();
            var duplicates = activeItems
                .GroupBy(x => x.DictValue?.Trim().ToUpper() ?? "")
                .Where(g => g.Count() > 1)
                .ToList();

            if (duplicates.Any())
            {
                var dupValue = duplicates.First().Key;
                await _msg.ShowWarningAsync($"保存失败！\n\n同一个字典下存在重复的真实键值：{dupValue}\n\n请修改保证唯一性。");
                return false;
            }

            return true;
        }

        protected override async Task AfterSaveSuccessAsync()
        {
            if (SelectedDictType != null && !string.IsNullOrWhiteSpace(SelectedDictType.DictCode))
            {
                await _dictCacheService.RefreshAsync(SelectedDictType.DictCode);
            }
        }
    }
}
