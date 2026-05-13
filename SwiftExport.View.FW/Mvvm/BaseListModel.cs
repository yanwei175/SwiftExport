
using GalaSoft.MvvmLight.Command;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace SwiftExport.View.FW.Mvvm
{
    public abstract class BaseListModel<T> : ViewModelBaseEx
        where T : BaseLineModel, new()
    {
        public ObservableCollection<T> Items { get; } = new ObservableCollection<T>();
        protected readonly List<T> _allItems = new List<T>();

        public event Action ListStateChanged;

        public BaseListModel()
        {
            Items.CollectionChanged += OnCollectionChanged;
        }

        private void OnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (T item in e.NewItems)
                {
                    if (!_allItems.Contains(item))
                        _allItems.Add(item);

                    item.PropertyChanged -= OnItemChanged;
                    item.PropertyChanged += OnItemChanged;
                }
            }

            if (e.OldItems != null)
            {
                foreach (T item in e.OldItems)
                {
                    item.PropertyChanged -= OnItemChanged;
                }
            }

            RaiseAllState();
        }

        private bool _suppressItemChangedEvents = false;

        //Items里的行变化事件,切换行才会触发.
        private void OnItemChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_suppressItemChangedEvents) return;

            if (e.PropertyName == nameof(BaseLineModel.IsValid))
                RaisePropertyChanged(nameof(IsAllValid));

            if (e.PropertyName == nameof(BaseLineModel.State))
            {
                RaiseDirtyChanged();//★ 列表级 通知脏数据以及脏数据的命令
            }

            if (e.PropertyName == nameof(BaseLineModel.IsChecked))
            {
                RaiseCheckedChanged();
            }
            RaiseAllState();
            ListStateChanged?.Invoke();
        }


 


        // ============================
        // 验证 & 脏数据
        // ============================
        public bool IsAllValid =>
            Items.Where(x => x.State != ModelState.Deleted).All(x => x.IsValid);
        public async Task<string> ValidateAllAsync()
        {
            foreach (var itemModel in Items)
            {
                var asyncError = await itemModel.ValidateAsync();
                if (!string.IsNullOrEmpty(asyncError))
                    return asyncError;
            }

            return null;
        }

        /// <summary>
        /// 列表级 Dirty（任何行 Added/Modified/Deleted）
        /// </summary>
        public bool IsDirty =>
            _allItems.Any(x =>
                x.State == ModelState.Added ||
                x.State == ModelState.Modified ||
                x.State == ModelState.Deleted);

        // ============================
        // Dirty 命令系统
        // ============================

        private readonly List<RelayCommand> _dirtyNotifyCommands = new List<RelayCommand>();

        public void RegisterDirtyNotifyCommand(RelayCommand command)
        {
            if (command != null && !_dirtyNotifyCommands.Contains(command))
                _dirtyNotifyCommands.Add(command);
        }

        private void RaiseDirtyChanged()
        {
            RaisePropertyChanged(nameof(IsDirty));

            foreach (var cmd in _dirtyNotifyCommands)
                cmd.RaiseCanExecuteChanged();
        }

        // ============================
        // 全选 / 反选
        // ============================
        public bool IsAllChecked
        {
            get => Items.Count > 0 && Items.All(x => x.IsChecked);
            set
            {
                _suppressItemChangedEvents = true;
                try
                {
                    foreach (var item in Items)
                        item.IsChecked = value;
                }
                finally
                {
                    _suppressItemChangedEvents = false;
                }
                RaiseCheckedChanged();
            }
        }

        public int CheckedCount => Items.Count(x => x.IsChecked);

        public void CheckAll() => IsAllChecked = true;
        public void UncheckAll() => IsAllChecked = false;
        public void ToggleCheckAll() => IsAllChecked = !IsAllChecked;
        
        public void InvertCheckAll()
        {
            _suppressItemChangedEvents = true;
            try
            {
                foreach (var item in Items)
                {
                    item.IsChecked = !item.IsChecked;
                }
            }
            finally
            {
                _suppressItemChangedEvents = false;
            }
            RaisePropertyChanged(nameof(IsAllChecked));
            RaisePropertyChanged(nameof(CheckedCount));
        }

        // ============================
        // Checked 命令系统（类似 Dirty）
        // ============================
        private readonly List<RelayCommand> _checkedNotifyCommands = new List<RelayCommand>();

        public void RegisterCheckedNotifyCommand(RelayCommand command)
        {
            if (command != null && !_checkedNotifyCommands.Contains(command))
                _checkedNotifyCommands.Add(command);
        }

        private void RaiseCheckedChanged()
        {
            RaisePropertyChanged(nameof(IsAllChecked));
            RaisePropertyChanged(nameof(CheckedCount));

            foreach (var cmd in _checkedNotifyCommands)
                cmd.RaiseCanExecuteChanged();
        }




        // ============================
        // 删除
        // ============================
        public void Remove(T item)
        {
            if (item == null) return;

            if (item.Id == 0)//如果Id为0，说明是新添加的行，直接从集合中移除
            {
                item.PropertyChanged -= OnItemChanged;
                _allItems.Remove(item);
                Items.Remove(item);
            }
            else //否则标记为Deleted状态，保留在集合中以便提交时处理
            {
                item.State = ModelState.Deleted;
                Items.Remove(item);//只从UI列表中移除，保留在_allItems中以便提交时处理
            }

            RaiseAllState();
        }

        public void Delete(T item)
        {
            if (item == null) return;
            item.PropertyChanged -= OnItemChanged;
            _allItems.Remove(item);
            Items.Remove(item);
            RaiseAllState();
        }


        public void Clear()
        {
            foreach (var item in _allItems)
                item.PropertyChanged -= OnItemChanged;
            Items.Clear();
            _allItems.Clear();
            RaiseAllState();
        }

        // ============================
        // ChangeSet
        // ============================
        public BatchRequestGeneric<T> GetChangeSet()
        {
            return new BatchRequestGeneric<T>
            {
                AddList = _allItems.Where(x => x.State == ModelState.Added).ToList(),
                UpdateList = _allItems.Where(x => x.State == ModelState.Modified).ToList(),
                DeleteIdList = _allItems.Where(x => x.State == ModelState.Deleted).Select(x => x.Id).ToList()
            };
        }
        public List<int> GetDeletedItems()
        {
            return _allItems.Where(x => x.State == ModelState.Deleted).Select(x => x.Id).ToList();
        }
        public List<T> GetCheckedItems()
        {
            return Items.Where(x => x.IsChecked).ToList();
        }
        public List<T> GetAllItems()
        {
            return _allItems.ToList();
        }


        public virtual void ReLoadRangeFromDtoList<Tdto>(List<Tdto> dtos)
        {
            // 清空现有数据后批量加载新数据以提升性能
            Clear();
            if (dtos != null && dtos.Count > 0)
            {
                // 预分配内部列表容量以减少扩容
                try { _allItems.Capacity = dtos.Count; } catch { }

                var models = new List<T>(dtos.Count);
                foreach (var dto in dtos)
                {
                    var model = new T();
                    // 在批量加载时抑制每行的 CommitChanges，统一在批量完成后提交
                    model.LoadFromDto(dto, 抑制清理脏数据: false);

                    models.Add(model);

                }

                //// 批量完成后统一提交每个 model 的变更（清理脏数据）
                //foreach (var m in models)
                //    m.CommitChanges();

                // 批量添加以减少 CollectionChanged 的重复触发
                AddRangeFromModelList(models);

            }

            // 最终刷新状态
            RaiseAllState();
        }
        /// <summary>
        /// 批量添加项以减少 CollectionChanged/通知开销。
        /// 此方法会在内部临时取消对 Items.CollectionChanged 的订阅，
        /// 手工将项加入到 _allItems 并订阅 PropertyChanged，然后一次性触发状态刷新。
        /// </summary>
        public void AddRangeFromModelList(IEnumerable<T> items)
        {
            if (items == null) return;

            // 暂时取消集合变更监听，避免对 UI/状态做重复刷新
            Items.CollectionChanged -= OnCollectionChanged;
            try
            {
                foreach (var item in items)
                {
                    if (item == null) continue;

                    // 策略 A：按数据库 ID 去重 (针对 Id > 0 的项)
                    if (item.Id > 0)
                    {
                        // 寻找池中是否有相同 ID 的项（包括那些已标记为 Deleted 的）
                        var existingById = _allItems.FirstOrDefault(x => x.Id == item.Id);
                        if (existingById != null)
                        {
                            // 场景：你删除了 ID 为 100 的行，现在由于某种原因（如刷新）又加载了 ID 为 100 的行。
                            // 必须踢掉旧的，否则后端会收到“删除 100”和“查询/更新 100”的冲突指令。
                            existingById.PropertyChanged -= OnItemChanged;
                            _allItems.Remove(existingById);
                            Items.Remove(existingById);
                        }
                    }
                    // 策略 B：按内存引用去重 (针对所有项，特别是 Id = 0 的新增项)
                    else if (_allItems.Contains(item))
                    {
                        // 如果这个【特定的内存实例】已经在池子里了，直接跳过，防止重复订阅事件。
                        continue;
                    }

                    // --- 执行添加 ---
                    Items.Add(item);
                    _allItems.Add(item);

                    // 防御性退订，彻底防止因重复加载数据遗留的旧委托
                    item.PropertyChanged -= OnItemChanged;
                    item.PropertyChanged += OnItemChanged;

                    // 即使是从数据库加载的，也有校验出错可能，刷新 UI
                    item.ForceValidationUI();
                }
            }
            finally
            {
                // 恢复集合变更监听
                Items.CollectionChanged += OnCollectionChanged;
            }
            // 批量完成后一次性刷新状态
            RaiseAllState();
        }
        public void AddModelToFirst(T item)
        {
            if (item == null) return;

            // 暂时取消集合变更监听，避免底层多重订阅
            Items.CollectionChanged -= OnCollectionChanged;
            try
            {
                // 1. 处理逻辑冲突：按 ID 查找并彻底清理旧实例
                if (item.Id > 0)
                {
                    var existingById = _allItems.FirstOrDefault(x => x.Id == item.Id);
                    if (existingById != null)
                    {
                        existingById.PropertyChanged -= OnItemChanged;
                        _allItems.Remove(existingById);
                        Items.Remove(existingById); // 确保 UI 也不重复
                    }
                }

                // 2. 处理内存冲突：如果这个【特定实例】已经在 UI 列表里了，先挪走
                if (Items.Contains(item))
                {
                    Items.Remove(item);
                }

                // 3. 始终确保它出现在两个列表的最前端
                Items.Insert(0, item);

                // 如果 _allItems 里已经有这个引用（可能刚才没被 Id 逻辑删掉），先删掉再插到首位
                if (_allItems.Contains(item))
                {
                    _allItems.Remove(item);
                }
                _allItems.Insert(0, item);

                // 防御性退订，防止同一对象多次引发事件
                item.PropertyChanged -= OnItemChanged;
                item.PropertyChanged += OnItemChanged;
                item.ForceValidationUI();
            }
            finally
            {
                // 恢复集合变更监听
                Items.CollectionChanged += OnCollectionChanged;
            }
  
            // 一次性刷新状态
            RaiseAllState();
        }
        protected void RaiseAllState()
        {
            RaiseDirtyChanged();// ★ 列表级 Dirty 自动通知命令
            RaisePropertyChanged(nameof(IsDirty));
            RaisePropertyChanged(nameof(IsAllValid));
            RaisePropertyChanged(nameof(IsAllChecked));
            RaisePropertyChanged(nameof(CheckedCount));
        }



        /// <summary>
        /// 检查全局池（包括已标记为删除的项）中是否已存在符合条件的项
        /// 示例 BomList.ExistsInAll(x => string.Equals(x.SubSku, p.Sku, StringComparison.OrdinalIgnoreCase));
        /// </summary>
        public bool ExistsInAll(Func<T, bool> predicate)
        {
            return _allItems.Any(predicate);
        }

        /// <summary>
        /// 获取全局池中符合条件的项（包括已标记为删除的项）
        /// 示例 BomList.GetFromAll(x => string.Equals(x.SubSku, p.Sku, StringComparison.OrdinalIgnoreCase));
        /// </summary>
        public T GetFromAll(Func<T, bool> predicate)
        {
            return _allItems.FirstOrDefault(predicate);
        }




    }


}
