using Mapster;
using SwiftExport.Contracts.Apis.SystemApis;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.UiInterface;
using SwiftExport.View.FW.DictViews.Models;
using SwiftExport.View.FW.Mvvm;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;

namespace SwiftExport.View.FW.DictViews.ViewModels
{
    public class DictManagerViewModel : BaseSmallDataManagerViewModel<DictDataModel, DictDataDto>
    {
        // ★ 1. 注入两个全新的 API 契约
        private readonly ISysDictTypeApi _dictTypeApi;
        private readonly ISysDictDataApi _dictDataApi;
        private readonly IDialogService _msg;

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
        public RelayCommand DeleteTypeCommand { get; }


        // ★ 2. 构造函数：注入新 API（将明细 API 传给基类，基类保存时要用到）
        public DictManagerViewModel(ISysDictTypeApi dictTypeApi, ISysDictDataApi dictDataApi, IDialogService dialogService)
            : base(dictDataApi, dialogService) // 假设你的基类接收 TApi 泛型或明细 API 用于保存
        {
            _msg = dialogService;
            _dictTypeApi = dictTypeApi;
            _dictDataApi = dictDataApi;

            DictTypes = new ObservableCollection<DictTypeModel>();
            DictTypesView = CollectionViewSource.GetDefaultView(DictTypes);
            AddTypeCommand = new RelayCommand(async () => await AddTypeAsync());
            DeleteTypeCommand = new RelayCommand(async () => await DeleteTypeAsync(), () => SelectedDictType != null);
            _ = LoadDictTypesAsync();
        }

        // ★ 3. 调用新的大类 API 获取所有分类
        private async Task LoadDictTypesAsync()
        {
            try
            {
                var rst = await _dictTypeApi.GetAllAsync(); // 调新接口
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

        // ★ 4. 调用新的明细 API 根据 Code 查询
        protected override async Task LoadDataAsync()
        {
            if (SelectedDictType == null)
            {
                _dispatcher.Invoke(() =>
                {
                    AllItems.Clear();
                    HasChanges = false;
                    StatusMessage = "请在左侧选择一个字典分类。";
                });
                return;
            }

            StatusMessage = $"正在加载【{SelectedDictType.DictName}】的枚举项...";
            try
            {
                var rst = await _dictDataApi.GetByCodeAsync(SelectedDictType.DictCode); // 调新接口

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
                        StatusMessage = $"加载成功，共 {AllItems.Count} 个枚举项。";
                    }
                    else
                    {
                        // ★ 出现网络异常时，也必须清空幽灵数据
                        AllItems.Clear();
                        StatusMessage = "加载明细失败: " + rst.Message;
                    }
                });
            }
            catch (Exception ex)
            {
                StatusMessage = "加载异常: " + ex.Message;
            }
        }
        private async Task AddTypeAsync()
        {
            // 1. 弹窗获取分类名称
            var newName = await _msg.ShowInputAsync("新建字典大类", "请输入字典分类名称 (如：客户类型)");
            if (string.IsNullOrWhiteSpace(newName)) return; // 用户点击了取消或没输入

            // 2. 弹窗获取分类编码
            var newCode = await _msg.ShowInputAsync("新建字典大类", "请输入字典分类唯一编码 (如：CustomerType)");
            if (string.IsNullOrWhiteSpace(newCode)) return;

            // 3. 构造 DTO
            var newTypeDto = new DictTypeDto
            {
                DictCode = newCode.Trim(),
                DictName = newName.Trim(),
                IsSystem = false // 前端新建的绝对不是系统内置的
            };

            StatusMessage = "正在创建新分类...";

            // 4. 调用 API 插入数据库
            var rst = await _dictTypeApi.CreateAsync(newTypeDto);

            if (rst.Success)
            {
                await _msg.ShowInfoAsync("新建大类成功！");
                await LoadDictTypesAsync(); // 重新加载左侧列表，新加的数据瞬间出现！
            }
            else
            {
                await _msg.ShowErrorAsync("新建大类失败：" + rst.Message);
            }
        }

        private async Task DeleteTypeAsync()
        {
            if (SelectedDictType == null) return;

            if (SelectedDictType.IsSystem)
            {
                await _msg.ShowWarningAsync("系统内置分类，禁止删除！");
                return;
            }

            if (AllItems.Any(x => x.State != ModelState.Deleted))
            {
                await _msg.ShowWarningAsync("该分类下还有明细数据，请先清空明细后再删除大类！");
                return;
            }

            bool isConfirm = await _msg.ShowConfirmAsync($"确定要彻底删除大类【{SelectedDictType.DictName}】吗？", "危险操作");
            if (!isConfirm) return;

            // ★ 直接调用大类 API 的泛型 DeleteAsync 接口！
            var rst = await _dictTypeApi.DeleteAsync(SelectedDictType.Id);
            if (rst.Success)
            {
                await LoadDictTypesAsync(); // 重新拉取列表
            }
            else
            {
                await _msg.ShowErrorAsync("删除失败：" + rst.Message);
            }
        }

        protected override void AddNewRow()
        {
            if (SelectedDictType == null)
            {
                _ = _msg.ShowWarningAsync("请先在左侧选择一个字典分类！");
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
                await _msg.ShowWarningAsync("存在未填写完整的枚举项！\n\n【显示名称】和【真实键值】为必填项。");
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
    }
}