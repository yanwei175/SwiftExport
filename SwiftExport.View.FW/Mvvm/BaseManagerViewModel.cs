using Mapster;
using SwiftExport.Contracts.Apis.BaseApi;
using SwiftExport.Contracts.UiInterface;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Threading;

namespace SwiftExport.View.FW.Mvvm
{
    public abstract class BaseManagerViewModel<TModel, TDto> : ViewModelBase
            where TModel : ViewModelBase, IBaseModel, new()
            where TDto : class
    {


        private readonly IDialogService _msg;

        private readonly IGenericApi<TDto> _api;
        private readonly Dispatcher _dispatcher;
        private CancellationTokenSource _debounceCts;

        public ObservableCollection<TModel> AllItems { get; set; }
        public ICollectionView ItemsView { get; private set; }

        protected BaseManagerViewModel(IGenericApi<TDto> api, IDialogService dialogService)
        {
            _msg = dialogService;
            _api = api;
            _dispatcher = Dispatcher.CurrentDispatcher;

            AllItems = new ObservableCollection<TModel>();
            ItemsView = CollectionViewSource.GetDefaultView(AllItems);

            LoadCommand = new RelayCommand(ExecuteRefresh);

            //LoadCommand = new RelayCommand(async () => await LoadDataAsync());

            AddNewRowCommand = new RelayCommand(AddNewRow);
            BatchSaveCommand = new RelayCommand(async () => await BatchSaveAsync());
            InvertCheckCommand = new RelayCommand(ExecuteInvertCheck);

            // ★ 命令绑定
            ItemCheckedChangedCommand = new RelayCommand(UpdateSelectionState);
            DeleteCheckedCommand = new RelayCommand(ExecuteDeleteChecked);
            SingleDeleteCommand = new RelayCommand<TModel>(ExecuteSingleDelete);


            // ★ 窃听器：监听集合内元素的增删改，实时更新全局保存按钮的状态
            AllItems.CollectionChanged += (s, e) =>
            {
                if (e.NewItems != null)
                {
                    foreach (INotifyPropertyChanged item in e.NewItems)
                        item.PropertyChanged += Item_PropertyChanged;
                }
                if (e.OldItems != null)
                {
                    foreach (INotifyPropertyChanged item in e.OldItems)
                        item.PropertyChanged -= Item_PropertyChanged;
                }
                EvaluateChanges();
            };


            _ = LoadDataAsync();
        }

        // ★ 当用户在表格里敲键盘修改某个单元格时，就会触发这个事件！
        private void Item_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // 忽略左侧勾选框的变化，只有真正改了数据才亮起保存按钮
            if (e.PropertyName == nameof(IBaseModel.IsChecked)) return;

            // 只要改了数据，立刻扫描是不是有脏数据
            EvaluateChanges();
        }

        // ================== 全局状态 ==================
        //脏数据状态 (驱动保存按钮亮起)
        private bool _hasChanges;
        public bool HasChanges
        {
            get => _hasChanges;
            set => SetProperty(ref _hasChanges, value);
        }

        // ★ 雷达扫描器：只要有新增的、或者被修改的数据，就亮起保存按钮！
        public void EvaluateChanges()
        {
            if (AllItems == null) return;
            HasChanges = AllItems.Any(x => x.State == ModelState.Added || x.State == ModelState.Modified);
        }


        // ================== 分页与搜索引擎 (纯数据驱动) ==================

        private bool _isRevertingPage = false;
        private int _pageIndex = 1;
        public int PageIndex
        {
            get => _pageIndex;
            set
            {
                if (_pageIndex == value) return; // 如果值没变，直接跳过
                // ★ 2. 如果是系统内部正在“撤销回滚”页码，只赋值，绝对不要去查数据库！
                if (_isRevertingPage)
                {
                    SetProperty(ref _pageIndex, value);
                    return;
                }

                if (SetProperty(ref _pageIndex, value))
                {
                    _ = LoadDataAsync(); // 页码改变，必定触发查询
                }
            }
        }

        private bool _isRevertingPageSize = false;
        private int _lastLoadedPageSize = 50; // 默认值要和你的 _pageSize 初始值一致

        private int _pageSize = 50;
        public int PageSize
        {
            get => _pageSize;
            set
            {
                if (_pageSize == value) return;
                // ★ 2. 如果是代码内部正在“撤销回滚”条数，只赋值，绝对不触发查询！
                if (_isRevertingPageSize)
                {
                    SetProperty(ref _pageSize, value);
                    return;
                }

                if (SetProperty(ref _pageSize, value))
                {
                    // ★ 架构师双保险：确保条件改变时，绝对能触发加载
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

        private string _searchText;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    DebounceSearch(_searchText);
                }
            }
        }

        private string _batchSearchTexts;
        public string BatchSearchTexts
        {
            get => _batchSearchTexts;
            set
            {
                if (SetProperty(ref _batchSearchTexts, value))
                {
                    // 批量词改变时，强制回第一页并查询
                    if (PageIndex == 1) _ = LoadDataAsync();
                    else PageIndex = 1;
                }
            }
        }

        private void DebounceSearch(string text)
        {
            _debounceCts?.Cancel();
            _debounceCts = new CancellationTokenSource();

            // 停顿 500ms 后发起真实查询
            Task.Delay(500, _debounceCts.Token).ContinueWith(t =>
            {
                if (t.IsCanceled) return;
                _dispatcher.Invoke(() =>
                {
                    if (PageIndex == 1) _ = LoadDataAsync();
                    else PageIndex = 1;
                });
            });
        }

        // ================== 核心模板方法 ==================

        /// <summary>
        /// 【留给子类实现】提取新增数据的特征键（例如：CustomerCode）。
        /// 用于在保存完毕后，自动使用这些键发起 BatchSearchTexts 过滤，防止新数据沉底。
        /// </summary>
        protected virtual IEnumerable<string> GetAddedItemKeys(IEnumerable<TModel> addedItems)
        {
            return Enumerable.Empty<string>();
        }

        // ================== 加载与保存业务逻辑 ==================
        private int _lastLoadedPageIndex = 1;
        protected virtual async Task LoadDataAsync()
        {
            // 防呆拦截：有没保存的脏数据时，警告用户
            if (HasChanges)
            {

                var ok = await _msg.ShowConfirmAsync("当前页有未保存的修改，强制刷新或翻页将丢失这些数据，是否继续？");

                if (!ok)
                {
                    // ★ 3. 用户取消翻页：同时上锁，并把【页码】和【条数】一起回滚！
                    _isRevertingPage = true;
                    _isRevertingPageSize = true;

                    // 回滚页码，防止 UI 和真实数据脱节

                    PageIndex = _lastLoadedPageIndex;
                    PageSize = _lastLoadedPageSize;

                    _isRevertingPage = false;
                    _isRevertingPageSize = false;
                    return;
                }
            }

            StatusMessage = "正在加载数据...";
            try
            {
                var request = new PagedRequestDto
                {
                    PageIndex = this.PageIndex,
                    PageSize = this.PageSize,
                    SearchText = this.SearchText,
                    BatchSearchTexts = this.BatchSearchTexts // 装载批量精准查询参数
                };

                var rst = await _api.GetPagedAsync(request);

                _dispatcher.Invoke(() =>
                {
                    if (rst.Success && rst.Data != null)
                    {
                        AllItems.Clear();

                        var models = rst.Data.Items.Adapt<List<TModel>>();
                        foreach (var model in models)
                        {
                            model.State = ModelState.Unchanged;
                            AllItems.Add(model);
                        }

                        TotalCount = rst.Data.TotalCount;
                        _lastLoadedPageIndex = this.PageIndex;
                        _lastLoadedPageSize = this.PageSize;

                        HasChanges = false; // 加载成功，熄灭保存按钮
                        // 确保翻页后全选状态被清空
                        IsAllChecked = false;
                        UpdateSelectionState();
                        StatusMessage = $"加载成功，共 {TotalCount} 条。";
                    }
                    else
                    {
                        StatusMessage = "加载失败: " + rst.Message;
                    }




                });
            }
            catch (Exception ex)
            {
                StatusMessage = "加载异常: " + ex.Message;
            }
        }
        private void ExecuteRefresh()
        {
            // 1. 如果用户刚才通过保存触发了 BatchSearchTexts（暗中过滤），
            // 刷新时必须帮他把这个隐藏条件抹除，否则他会被困在新数据视图里出不来。
            // 注意：这里必须直接操作后台字段 _batchSearchTexts，
            // 如果用属性 BatchSearchTexts = null，会触发 Setter 导致多查一次数据库！
            if (!string.IsNullOrEmpty(_batchSearchTexts))
            {
                _batchSearchTexts = null;
                OnPropertyChanged(nameof(BatchSearchTexts));
            }

            // 2. 如果你想让刷新按钮连带把用户的模糊搜索框也清空，可以取消下面这行的注释：
            // _searchText = null;
            // OnPropertyChanged(nameof(SearchText));

            // 3. 原汁原味地调用纯粹的 LoadData
            _ = LoadDataAsync();
        }

        // 默认返回 true，子类可重写拦截保存
        protected virtual async Task<bool> ValidateBeforeSaveAsync(List<TModel> dirtyModels)
        {
            // 基类默认直接返回 true放行
            // 加 await Task.FromResult 是为了消除编译器的 CS1998 警告
            return await Task.FromResult(true);
        }

        private async Task BatchSaveAsync()
        {
            // 1. 抓取所有脏数据
            var dirtyModels = AllItems.Where(x => x.State == ModelState.Added || x.State == ModelState.Modified).ToList();
            if (!dirtyModels.Any())
            {
                await _msg.ShowInfoAsync("没有需要保存的变动,");
                return;
            }

            // 2. 校验表单完整性
            if (dirtyModels.Any(x => !x.IsValid))
            {
                await _msg.ShowWarningAsync("存在未填写完整的数据，请修正标红的单元格！"); return;
            }
            bool isValid = await ValidateBeforeSaveAsync(dirtyModels);
            if (!isValid) return;

            // 3. 提取纯新增的数据，决定后续的路由策略
            var addedItems = dirtyModels.Where(x => x.State == ModelState.Added).ToList();
            bool hasNewAddedItems = addedItems.Any();

            StatusMessage = "正在保存...";

            // 4. 组装发给后端的真实请求
            var request = new BatchRequestGeneric<TDto>
            {
                AddList = addedItems.Adapt<List<TDto>>(),
                UpdateList = dirtyModels.Where(x => x.State == ModelState.Modified).Adapt<List<TDto>>()
            };

            try
            {
                // 5. 真实 API 调用
                var rst = await _api.BatchSaveAsync(request);

                if (rst.Success)
                {
                    // ★ 极其重要：先假装全部变成干净数据，防止下面触发 LoadDataAsync 时弹窗拦截！
                    foreach (var item in dirtyModels) item.State = ModelState.Unchanged;
                    HasChanges = false;

                    // 6. 智能路由分发
                    if (hasNewAddedItems)
                    {
                        // 场景 A：有新增数据，提取特征值进行精确回显
                        var newKeys = GetAddedItemKeys(addedItems)
                                      .Where(k => !string.IsNullOrWhiteSpace(k))
                                      .Distinct()
                                      .ToList();

                        if (newKeys.Any())
                        {
                            await _msg.ShowInfoAsync("保存成功！已自动筛选出您刚刚新增的数据。");
                            // 瞬间触发上方 BatchSearchTexts 的 Setter，并执行完美刷新！
                            this.BatchSearchTexts = string.Join(",", newKeys);
                        }
                        else
                        {
                            await _msg.ShowInfoAsync("保存成功！");
                            if (PageIndex == 1) _ = LoadDataAsync();
                            else PageIndex = 1;
                        }
                    }
                    else
                    {
                        // 场景 B：全是修改数据。原地刷新当前页
                        await _msg.ShowInfoAsync("保存成功！");
                        _ = LoadDataAsync();
                    }
                }
                else
                {
                    await _msg.ShowErrorAsync("保存失败：" + rst.Message);
                }
            }
            catch (Exception ex)
            {
                await _msg.ShowErrorAsync("异常: " + ex.Message);
            }
        }


        private bool _hasSelectedItems;
        public bool HasSelectedItems
        {
            get => _hasSelectedItems;
            set => SetProperty(ref _hasSelectedItems, value);
        }

        private int _selectedCount;
        public int SelectedCount
        {
            get => _selectedCount;
            set => SetProperty(ref _selectedCount, value);
        }

        private string _statusMessage = "准备就绪";
        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(nameof(StatusMessage)); }
        }

        private bool _isAllChecked;
        public bool IsAllChecked
        {
            get => _isAllChecked;
            set
            {
                if (SetProperty(ref _isAllChecked, value) && AllItems != null)
                {
                    foreach (var item in AllItems) item.IsChecked = value;
                    UpdateSelectionState();
                }
            }
        }


        // ================== 命令声明 ==================
        public RelayCommand LoadCommand { get; }
        public RelayCommand AddNewRowCommand { get; }
        public RelayCommand BatchSaveCommand { get; }
        public RelayCommand InvertCheckCommand { get; }

        // 处理 CheckBox 点击 和 真实删除
        public RelayCommand ItemCheckedChangedCommand { get; }
        public RelayCommand DeleteCheckedCommand { get; }
        public RelayCommand<TModel> SingleDeleteCommand { get; } // 保留给右键菜单用




        private void AddNewRow()
        {
            var newModel = new TModel { Id = 0, State = ModelState.Added };
            AllItems.Insert(0, newModel);
            StatusMessage = "已添加新行，请录入数据。";
        }
        // 真实删除逻辑 (智能判断单删还是批删)
        private async void ExecuteDeleteChecked()
        {
            var checkedItems = AllItems.Where(x => x.IsChecked).ToList();
            if (checkedItems.Count <= 0) return;



            StatusMessage = "正在删除...";
            try
            {
                // 1. 先把本地刚新建、还没保存过的数据直接剔除
                var unsavedItems = checkedItems.Where(x => x.State == ModelState.Added).ToList();
                foreach (var item in unsavedItems) AllItems.Remove(item);

                // 2. 提取出真正存在于数据库中的数据
                var dbItems = checkedItems.Where(x => x.State != ModelState.Added).ToList();
                if (dbItems.Count > 0)
                {
                    if (!await _msg.ShowConfirmAsync($"确定要永久删除这 {dbItems.Count} 行数据吗？此操作不可逆！")) return;

                    bool isSuccess = false;
                    string errMsg = "";

                    // ★ 智能分发：1个走 Delete，多个走 BatchSave
                    if (dbItems.Count == 1)
                    {
                        var rst = await _api.DeleteAsync(dbItems.First().Id);
                        isSuccess = rst.Success;
                        errMsg = rst.Message;
                    }
                    else
                    {
                        var req = new BatchRequestGeneric<TDto> { DeleteIdList = dbItems.Select(x => x.Id).ToList() };
                        var rst = await _api.BatchSaveAsync(req);
                        isSuccess = rst.Success;
                        errMsg = rst.Message;
                    }

                    if (isSuccess)
                    {
                        // 成功后从界面上抹除
                        foreach (var item in dbItems) AllItems.Remove(item);
                        await _msg.ShowInfoAsync("删除成功！");
                    }
                    else
                    {
                        await _msg.ShowErrorAsync("删除失败：" + errMsg);
                        return; // 失败了就不要刷新状态，保留原样
                    }
                }
            }
            catch (Exception ex) { await _msg.ShowErrorAsync("删除异常：" + ex.Message); }
            finally
            {
                StatusMessage = "准备就绪";
                UpdateSelectionState(); // 无论成功失败，重算一下选中的数量
            }
        }

        // 右键菜单单删用的
        private async void ExecuteSingleDelete(TModel model)
        {
            if (model == null) return;
            model.IsChecked = true; // 强行打勾，复用批删逻辑
            ExecuteDeleteChecked();
        }
        //反选
        private void ExecuteInvertCheck()
        {
            foreach (var item in AllItems) item.IsChecked = !item.IsChecked;
            UpdateSelectionState();
        }

        // ★ 核心发动机
        public void UpdateSelectionState()
        {
            if (AllItems == null) return;

            SelectedCount = AllItems.Count(x => x.IsChecked);
            HasSelectedItems = SelectedCount > 0;

            _isAllChecked = SelectedCount == AllItems.Count && AllItems.Count > 0;
            OnPropertyChanged(nameof(IsAllChecked));
        }



        // ================== 搜索过滤 ==================
        //protected abstract bool FilterItem(TModel item, string searchText);

        //private void DebounceFilter(string text)
        //{
        //    _debounceCts?.Cancel();
        //    _debounceCts = new CancellationTokenSource();
        //    Task.Delay(350, _debounceCts.Token).ContinueWith(t =>
        //    {
        //        if (t.IsCanceled) return;
        //        _dispatcher.Invoke(() =>
        //        {
        //            ItemsView.Filter = (obj) => string.IsNullOrWhiteSpace(text) || FilterItem((TModel)obj, text);
        //            ItemsView.Refresh();
        //        });
        //    });
        //}



    }
}