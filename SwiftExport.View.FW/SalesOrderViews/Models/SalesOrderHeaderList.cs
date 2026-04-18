using SwiftExport.Contracts.DTOs.SalesOrders;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.View.FW.Mvvm;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace SwiftExport.View.FW.SalesOrderViews.Models
{
    /// <summary>
    /// 销售订单头列表模型。
    /// 用于订单管理页承载“大量订单头”的集合，不负责任何明细行计算。
    /// </summary>
    public class SalesOrderHeaderList : ViewModelBaseEx
    {
        /// <summary>
        /// UI 可见的订单头集合（用于 DataGrid 绑定）
        /// </summary>
        public ObservableCollection<SalesOrderHeaderModel> Items { get; } = new ObservableCollection<SalesOrderHeaderModel>();

        /// <summary>
        /// 内部所有订单头（包括 Deleted）
        /// 用于批量删除、ChangeSet、脏数据等业务逻辑
        /// </summary>
        private readonly List<SalesOrderHeaderModel> _allItems = new List<SalesOrderHeaderModel>();

        public SalesOrderHeaderList()
        {
            Items.CollectionChanged += OnCollectionChanged;
        }

        /// <summary>
        /// 从 DTO 列表加载订单头集合（用于订单管理页）
        /// </summary>
        public void LoadFromDto(List<SalesOrderHeaderDto> dtos)
        {
            Items.Clear();
            _allItems.Clear();

            if (dtos == null || dtos.Count == 0)
            {
                RaisePropertyChanged(nameof(IsDirty));
                RaisePropertyChanged(nameof(IsAllValid));
                return;
            }

            foreach (var dto in dtos)
            {
                var header = new SalesOrderHeaderModel();
                header.LoadFromDto(dto);
                Items.Add(header);
            }

            RaisePropertyChanged(nameof(IsDirty));
            RaisePropertyChanged(nameof(IsAllValid));
        }

        // ============================================================
        // 集合变更事件（新增/删除订单头）
        // ============================================================
        private void OnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (SalesOrderHeaderModel header in e.NewItems)
                {
                    if (!_allItems.Contains(header))
                        _allItems.Add(header);

                    header.PropertyChanged += OnHeaderPropertyChanged;
                }
            }

            if (e.OldItems != null)
            {
                foreach (SalesOrderHeaderModel header in e.OldItems)
                {
                    header.PropertyChanged -= OnHeaderPropertyChanged;
                }
            }

            RaisePropertyChanged(nameof(IsDirty));
            RaisePropertyChanged(nameof(IsAllValid));
        }

        // ============================================================
        // 订单头变化事件
        // ============================================================
        private void OnHeaderPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            RaisePropertyChanged(nameof(IsAllValid));

            if (e.PropertyName == nameof(SalesOrderHeaderModel.State))
                RaisePropertyChanged(nameof(IsDirty));
        }

        // ============================================================
        // 自动验证（所有订单头是否有效）
        // ============================================================
        public bool IsAllValid => _allItems
            .Where(x => x.State != ModelState.Deleted)
            .All(x => x.IsValid);

        public async Task<string> ValidateAllAsync()
        {
            foreach (var header in Items)
            {
                var asyncError = await header.ValidateAsync();
                if (!string.IsNullOrEmpty(asyncError))
                    return asyncError;
            }

            return null;
        }

        // ============================================================
        // 脏数据检测（Dirty）
        // ============================================================
        public bool IsDirty =>
            _allItems.Any(x =>
                x.State == ModelState.Added ||
                x.State == ModelState.Modified ||
                x.State == ModelState.Deleted);

        // ============================================================
        // 删除订单头（软删除）
        // ============================================================
        public void Delete(SalesOrderHeaderModel header)
        {
            if (header == null) return;

            if (header.Id == 0)
            {
                _allItems.Remove(header);
                Items.Remove(header);
                RaisePropertyChanged(nameof(IsDirty));
                RaisePropertyChanged(nameof(IsAllValid));
                return;
            }

            header.State = ModelState.Deleted;
            Items.Remove(header);

            RaisePropertyChanged(nameof(IsDirty));
            RaisePropertyChanged(nameof(IsAllValid));
        }

        // ============================================================
        // ChangeSet（一次性提交）
        // ============================================================
        public BatchRequestGeneric<SalesOrderHeaderModel> GetChangeSet()
        {
            return new BatchRequestGeneric<SalesOrderHeaderModel>
            {
                AddList = _allItems.Where(x => x.State == ModelState.Added).ToList(),
                UpdateList = _allItems.Where(x => x.State == ModelState.Modified).ToList(),
                DeleteIdList = _allItems.Where(x => x.State == ModelState.Deleted).Select(x => x.Id).ToList()
            };
        }
    }
}
