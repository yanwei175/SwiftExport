using Mapster;
using SwiftExport.Contracts.Apis.SystemApis; // 假设你的 API 命名空间
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.VSTO.UserWindow.DictViews.Models;
using SwiftExport.VSTO.UserWindow.Mvvm;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;

namespace SwiftExport.VSTO.UserWindow.DictViews.ViewModels
{
    // 注意：这里的泛型对应的是右侧的明细表 (Detail)
    public class DictManagerViewModel : BaseSmallDataManagerViewModel<DictDataModel, DictDataDto>
    {
        // 我们需要一个更具体的 API 接口，因为它不仅能查明细，还能查左侧的大类
        private readonly IDictApi _dictApi;

        // ================= 左侧大类 (Master) 相关属性 =================

        public ObservableCollection<DictTypeModel> DictTypes { get; set; }
        public ICollectionView DictTypesView { get; private set; }

        private DictTypeModel _selectedDictType;
        public DictTypeModel SelectedDictType
        {
            get => _selectedDictType;
            set
            {
                // ★ 极其关键的防呆拦截：如果右侧有未保存的脏数据，不准他切换左侧！
                if (HasChanges && _selectedDictType != null && _selectedDictType != value)
                {
                    if (!SwiftExport.VSTO.UserWindow.Dialog.Msg.ShowQuestion("当前字典有未保存的修改，切换分类将丢失这些数据，是否继续？"))
                    {
                        // 用户选了“否”，不让它切过去，并且触发通知让 UI 的高亮弹回来
                        OnPropertyChanged(nameof(SelectedDictType));
                        return;
                    }
                }

                if (SetProperty(ref _selectedDictType, value))
                {
                    // ★ 魔法桥接：一旦左侧选中的大类变了，立刻触发右侧表格的数据刷新！
                    _ = LoadDataAsync();
                }
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
                    // 左侧大类列表的本地实时过滤
                    DictTypesView.Filter = obj =>
                    {
                        if (string.IsNullOrWhiteSpace(value)) return true;
                        var m = (DictTypeModel)obj;
                        return (m.DictName?.Contains(value, StringComparison.OrdinalIgnoreCase) == true) ||
                               (m.DictCode?.Contains(value, StringComparison.OrdinalIgnoreCase) == true);
                    };
                    DictTypesView.Refresh();
                }
            }
        }


        // ================= 构造函数 =================

        public DictManagerViewModel(IDictApi api) : base(api)
        {
            _dictApi = api;
            DictTypes = new ObservableCollection<DictTypeModel>();
            DictTypesView = CollectionViewSource.GetDefaultView(DictTypes);

            // 初始化时，先把左侧的字典大类拉出来
            _ = LoadDictTypesAsync();
        }


        // ================= 核心重写逻辑 =================

        /// <summary>
        /// 专门拉取左侧大类数据
        /// </summary>
        private async Task LoadDictTypesAsync()
        {
            try
            {
                var rst = await _dictApi.GetAllDictTypesAsync(); // 假设你的API有这个方法
                if (rst.Success && rst.Data != null)
                {
                    _dispatcher.Invoke(() =>
                    {
                        DictTypes.Clear();
                        var models = rst.Data.Adapt<List<DictTypeModel>>();
                        foreach (var m in models) DictTypes.Add(m);

                        // 默认选中第一行，这会自动触发属性 Setter 里的 LoadDataAsync 去拉右侧明细！
                        SelectedDictType = DictTypes.FirstOrDefault();
                    });
                }
            }
            catch (Exception ex)
            {
                StatusMessage = "加载字典分类失败: " + ex.Message;
            }
        }

        /// <summary>
        /// 重写基类的加载数据逻辑 (因为基类是 GetAll，我们现在是 GetByDictCode)
        /// </summary>
        protected override async Task LoadDataAsync()
        {
            if (SelectedDictType == null) return;

            StatusMessage = $"正在加载【{SelectedDictType.DictName}】的枚举项...";
            try
            {
                // ★ 这里的 API 调用变成了：根据左侧选中的 DictCode 获取明细
                var rst = await _dictApi.GetDictDataByCodeAsync(SelectedDictType.DictCode);

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
                        DebounceFilter(SearchText); // 右侧搜索框过滤
                        StatusMessage = $"加载成功，共 {AllItems.Count} 个枚举项。";
                    }
                    else
                    {
                        StatusMessage = "加载明细失败: " + rst.Message;
                    }
                });
            }
            catch (Exception ex)
            {
                StatusMessage = "加载异常: " + ex.Message;
            }
        }

        /// <summary>
        /// 重写新增行逻辑：自动注入当前选中的大类 Code，并默认 IsSystem=false
        /// </summary>
        protected override void AddNewRow()
        {
            if (SelectedDictType == null)
            {
                SwiftExport.VSTO.UserWindow.Dialog.Msg.ShowWarning("请先在左侧选择一个字典分类！");
                return;
            }

            // 让基类先去 new 一个空行并插到最前面
            base.AddNewRow();

            // 然后我们把刚才新加的那行揪出来，给它打上专属烙印
            var newlyAdded = AllItems.First();
            newlyAdded.DictCode = SelectedDictType.DictCode; // 强行绑定到左侧选中的大类！
            newlyAdded.IsSystem = false; // 用户新建的，绝对不能是系统内置的！

            // 为了美观，排序号默认给个大数字，或者取当前最大值+1
            newlyAdded.SortOrder = AllItems.Count > 1 ? AllItems.Max(x => x.SortOrder) + 10 : 10;
        }

        /// <summary>
        /// 右侧明细表的本地过滤规则
        /// </summary>
        protected override bool FilterItem(DictDataModel item, string searchText)
        {
            return (item.DictLabel?.Contains(searchText, StringComparison.OrdinalIgnoreCase) == true) ||
                   (item.DictValue?.Contains(searchText, StringComparison.OrdinalIgnoreCase) == true);
        }

        /// <summary>
        /// 保存前的防呆拦截
        /// </summary>
        protected override bool ValidateBeforeSave(List<DictDataModel> dirtyModels)
        {
            // 拦截一：空值校验
            if (dirtyModels.Any(x => !x.IsValid))
            {
                SwiftExport.VSTO.UserWindow.Dialog.Msg.ShowWarning("存在未填写完整的枚举项！\n\n【显示名称】和【真实键值】为必填项。");
                return false;
            }

            // 拦截二：查重校验 (检查同一个大类下，DictValue 键值绝对不能重复！)
            var activeItems = AllItems.Where(x => x.State != ModelState.Deleted).ToList();
            var duplicates = activeItems
                .GroupBy(x => x.DictValue?.Trim().ToUpper() ?? "")
                .Where(g => g.Count() > 1)
                .ToList();

            if (duplicates.Any())
            {
                var dupValue = duplicates.First().Key;
                SwiftExport.VSTO.UserWindow.Dialog.Msg.ShowWarning($"保存失败！\n\n同一个字典下存在重复的真实键值：{dupValue}\n\n请修改保证唯一性。");
                return false;
            }

            return true;
        }
    }
}