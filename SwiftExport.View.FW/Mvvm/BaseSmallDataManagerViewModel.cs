using GalaSoft.MvvmLight.Command;
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
    /// <summary>
    /// 小型数据字典管理器基类 (无分页，一次性加载，本地内存过滤，行内标记删除)
    /// </summary>
    public abstract class BaseSmallDataManagerViewModel<TModel, TDto> : ViewModelBaseEx
            where TModel : ViewModelBaseEx, IBaseModel, new()
            where TDto : class
    {
        private readonly IDialogService _msg;
        protected readonly IGenericApi<TDto> _api;
        // 中文注释：保留给历史派生类兼容使用，新的代码请优先走 DispatcherProvider。
        protected readonly Dispatcher _dispatcher;
        private CancellationTokenSource _debounceCts;

        public ObservableCollection<TModel> AllItems { get; set; }
        public ICollectionView ItemsView { get; private set; }

        protected BaseSmallDataManagerViewModel(IGenericApi<TDto> api, IDialogService dialogService)
        {
            _msg = dialogService;
            _api = api;
            _dispatcher = DispatcherProvider.UI ?? Dispatcher.CurrentDispatcher;

            AllItems = new ObservableCollection<TModel>();
            ItemsView = CollectionViewSource.GetDefaultView(AllItems);

            LoadCommand = new RelayCommand(async () => await LoadDataAsync());
            AddNewRowCommand = new RelayCommand(AddNewRow);
            BatchSaveCommand = new RelayCommand(async () => await BatchSaveAsync());

            // ★ 行内操作命令
            SingleDeleteCommand = new RelayCommand<TModel>(ExecuteSingleDelete);
            CancelDeleteCommand = new RelayCommand<TModel>(ExecuteCancelDelete);

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

        private void Item_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // 只要改了数据，立刻扫描是不是有脏数据
            EvaluateChanges();
        }

        // ================== 全局状态 ==================

        private bool _hasChanges;
        public bool HasChanges
        {
            get => _hasChanges;
            set => SetProperty(ref _hasChanges, value);
        }

        // ★ 雷达扫描器：只要有 新增、修改、标记删除 的数据，就亮起保存按钮
        public void EvaluateChanges()
        {
            if (AllItems == null) return;
            HasChanges = AllItems.Any(x => x.State == ModelState.Added ||
                                           x.State == ModelState.Modified ||
                                           x.State == ModelState.Deleted);
        }

        private string _statusMessage = "准备就绪";
        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; RaisePropertyChanged(nameof(StatusMessage)); }
        }

        // ================== 本地搜索与过滤 ==================

        private string _searchText;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    DebounceFilter(value); // 触发本地过滤，不发请求！
                }
            }
        }

        /// <summary>
        /// 【留给子类实现】定义本地模糊过滤的规则
        /// </summary>
        protected abstract bool FilterItem(TModel item, string searchText);

        protected void DebounceFilter(string text)
        {
            _debounceCts?.Cancel();
            _debounceCts = new CancellationTokenSource();

            Task.Delay(350, _debounceCts.Token).ContinueWith(t =>
            {
                if (t.IsCanceled) return;

                // 中文注释：过滤刷新统一走线程安全调度，避免跨线程触发 ICollectionView 异常。
                DispatcherProvider.Invoke(() =>
                {
                    ItemsView.Filter = (obj) => string.IsNullOrWhiteSpace(text) || FilterItem((TModel)obj, text);
                    ItemsView.Refresh();
                });
            }, TaskScheduler.Default);
        }

        // ================== 加载与保存 ==================

        protected virtual async Task LoadDataAsync()
        {
            if (HasChanges)
            {
                if (!await _msg.ShowConfirmAsync("当前有未保存的修改，刷新将丢失这些数据，是否继续？")) return;
            }

            StatusMessage = "正在加载数据...";
            try
            {
                var rst = await _api.GetAllAsync();

                // 中文注释：统一走 DispatcherProvider，保证在宿主切换时仍安全更新 UI 绑定集合。
                DispatcherProvider.Invoke(() =>
                {
                    if (rst.Success && rst.Data != null)
                    {
                        AllItems.Clear();

                        var models = rst.Data.Adapt<List<TModel>>();
                        foreach (var model in models)
                        {
                            model.State = ModelState.Unchanged;
                            AllItems.Add(model);
                        }

                        HasChanges = false;

                        // 重新应用本地过滤
                        DebounceFilter(SearchText);

                        StatusMessage = $"加载成功，共 {AllItems.Count} 条数据。";
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

        /// <summary>
        /// 默认返回 true，子类可重写拦截保存
        /// </summary>
        protected virtual async Task<bool> ValidateBeforeSaveAsync(List<TModel> dirtyModels)
        {
            // 基类默认直接返回 true放行
            // 加 await Task.FromResult 是为了消除编译器的 CS1998 警告
            return await Task.FromResult(true);
        }

        protected virtual Task AfterSaveSuccessAsync()
        {
            return Task.FromResult(true);
        }

        private async Task BatchSaveAsync()
        {
            // 抓取所有脏数据 (新增、修改、标记删除)
            var dirtyModels = AllItems.Where(x => x.State == ModelState.Added ||
                                                  x.State == ModelState.Modified ||
                                                  x.State == ModelState.Deleted).ToList();

            if (!dirtyModels.Any()) { await _msg.ShowInfoAsync("没有需要保存的变动。"); return; }

            // ★ 核心细节：校验时不包含被标记删除的行 (因为它们都要被删了，填没填完已经不重要了)
            var modelsToValidate = dirtyModels.Where(x => x.State != ModelState.Deleted).ToList();

            if (modelsToValidate.Any(x => !x.IsValid))
            {
                await _msg.ShowWarningAsync("存在未填写完整的数据."); return;
            }

            bool isValid = await ValidateBeforeSaveAsync(modelsToValidate);
            if (!isValid) return;

            var ok = await _msg.ShowConfirmAsync("确定要保存吗？");
            if (!ok) return;

            StatusMessage = "正在保存...";

            // 组装批量请求
            var request = new BatchRequestGeneric<TDto>
            {
                AddList = dirtyModels.Where(x => x.State == ModelState.Added).Adapt<List<TDto>>(),
                UpdateList = dirtyModels.Where(x => x.State == ModelState.Modified).Adapt<List<TDto>>(),
                // ★ 把标记删除的行的 Id 提取出来发送给后端
                DeleteIdList = dirtyModels.Where(x => x.State == ModelState.Deleted).Select(x => x.Id).ToList()
            };

            try
            {

                var rst = await _api.BatchSaveAsync(request);

                if (rst.Success)
                {
                    await _msg.ShowInfoAsync("保存成功！");
                    // 这种带本地软删除的逻辑，保存成功后直接全量刷新一遍是最稳妥的
                    await LoadDataAsync();
                    await AfterSaveSuccessAsync();
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

        // ================== 新增与行内删除控制 ==================

        protected virtual void AddNewRow()
        {
            var newModel = new TModel { Id = 0, State = ModelState.Added };
            AllItems.Insert(0, newModel);
            StatusMessage = "已添加新行，请录入数据。";

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                SearchText = string.Empty;
            }
        }

        /// <summary>
        /// 点击“删除”按钮
        /// </summary>
        private void ExecuteSingleDelete(TModel model)
        {
            if (model == null) return;

            if (model.State == ModelState.Added)
            {
                // ★ 极其智能：如果这行数据是刚新建还没保存的，直接从 UI 抹掉，不用打标记
                AllItems.Remove(model);
            }
            else
            {
                // ★ 否则，把它标记为 Deleted。由于 XAML 里有 Trigger，这行瞬间就会变红
                model.State = ModelState.Deleted;
            }

            EvaluateChanges(); // 触发保存按钮亮起
        }

        /// <summary>
        /// 点击“撤销”按钮
        /// </summary>
        private void ExecuteCancelDelete(TModel model)
        {
            if (model == null) return;

            if (model.State == ModelState.Deleted)
            {
                // 撤销删除，恢复为未修改状态
                model.State = ModelState.Unchanged;
                EvaluateChanges();
            }
        }

        // ================== 命令声明 ==================
        public RelayCommand LoadCommand { get; }
        public RelayCommand AddNewRowCommand { get; }
        public RelayCommand BatchSaveCommand { get; }

        // 行内专属命令
        public RelayCommand<TModel> SingleDeleteCommand { get; }
        public RelayCommand<TModel> CancelDeleteCommand { get; }
    }
}