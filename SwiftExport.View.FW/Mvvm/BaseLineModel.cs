using Mapster;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace SwiftExport.View.FW.Mvvm
{

    /// <summary>
    /// 所有“行模型”的基类。
    /// 功能：
    /// 1. 自动状态机（Unchanged / Modified / Added / Deleted）
    /// 2. 自动区分“用户修改 vs 程序计算”
    /// 3. 自动 ChangeTracker（记录原始值、撤销、提交）
    /// 4. 自动验证体系（字段级 + 行级 + 异步验证）
    /// 5. 不影响任何 XAML（继续使用 IDataErrorInfo）
    /// </summary>

    public abstract class BaseLineModel : ViewModelBaseEx, IBaseModel, IDataErrorInfo

    {
        /// <summary>
        /// 从 DTO 加载数据到 Model。
        /// </summary>
        /// <param name="dto"></param>
        /// <param name="抑制清理脏数据">是否抑制清理脏数据,默认false为不抑制,会把状态重置为 Unchanged</param>
        public virtual void LoadFromDto(object dto, bool 抑制清理脏数据 = false)
        {
            if (dto == null) return;

            using (DisabledStateScope())
            {
                //找到Model的类型,用Adapt转换
                dto.Adapt(this, dto.GetType(), GetType());
                if (this.Uid == Guid.Empty)
                { this.MarkAsAdded(); }

                // 根据调用方需要决定是否立即提交/清理脏数据
                if (!抑制清理脏数据)
                    CommitChanges();
            }
        }

        #region   后端字段
        private Guid _uid;
        public Guid Uid
        {
            get => _uid;
            set => SetProperty(ref _uid, value);
        }

        private int _version = 1;
        public int Version
        {
            get => _version;
            set => SetProperty(ref _version, value);
        }
        private DateTime? _createdDate;
        public DateTime? CreatedDate
        {
            get => _createdDate;
            set => SetProperty(ref _createdDate, value);
        }

        private DateTime? _updatedDate;
        public DateTime? UpdatedDate
        {
            get => _updatedDate;
            set => SetProperty(ref _updatedDate, value);
        }
        private string _createdBy;
        public string CreatedBy
        {
            get => _createdBy;
            set => SetProperty(ref _createdBy, value);
        }

        private string _updatedBy;
        public string UpdatedBy
        {
            get => _updatedBy;
            set => SetProperty(ref _updatedBy, value);
        }
        #endregion

        #region   前端属性
        private ModelState _state = ModelState.Unchanged;
        public ModelState State
        {
            get => _state;
            set => SetProperty(ref _state, value);
        }

        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                //是否选中不应该触发脏数据变化.
                using (DisabledStateScope())
                {
                    SetProperty(ref _isChecked, value);
                }
            }
        }

        public bool IsDirty => State != ModelState.Unchanged;

        #endregion



        // ============================================================
        // 2. 程序内部修改抑制器（用于计算属性, LoadFromDto, RollbackChanges 等）
        // ============================================================

        private class ActionOnDispose : IDisposable
        {
            private readonly Action _action;
            public ActionOnDispose(Action action) => _action = action;
            public void Dispose() => _action?.Invoke();
        }


        // ===========================
        // 3. ChangeTracker（原始值跟踪）
        // ===========================
        private readonly Dictionary<string, object> _原始值字典 = new Dictionary<string, object>();


        // ============================================================
        // 4. 重写 SetProperty（隐藏父类）
        // ============================================================

        /// <summary>
        /// 赋值 + 通知 + 自动状态机 + 自动 ChangeTracker。
        /// 这是行模型最核心的逻辑。
        /// 
        /// </summary>
        protected override bool SetProperty<T>(ref T storage, T value,
            [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(storage, value))
                return false;

            // 记录原始值（只记录一次）
            if (!_原始值字典.ContainsKey(propertyName))
                _原始值字典[propertyName] = storage;

            storage = value;
            RaisePropertyChanged(propertyName);
            RaisePropertyChanged(nameof(IsValid));//只要有属性值变化，必须通知 IsValid 重新计算！
            RaisePropertyChanged("Item[]"); // 【关键修复】通知 WPF 绑定引擎重新计算 IDataErrorInfo 的 this[propName]

            // 用户修改才触发 Modified
            if (DisabledState)
                return true;

            MarkAsModified();
            return true;
        }


        // ============================================================
        // 5. 状态机
        // ============================================================
        protected virtual void MarkAsModified()
        {
            if (DisabledState)
                return;
            if (State == ModelState.Unchanged)
                State = ModelState.Modified;
        }

        public void MarkAsAdded()
        {
            State = ModelState.Added;
            RaisePropertyChanged(nameof(State)); // 通知列表 Dirty
        }
        public void MarkAsDeleted()
        {
            State = ModelState.Deleted;
            RaisePropertyChanged(nameof(State)); // 通知列表 Dirty
        }



        // ============================================================
        // 6. 自动验证体系（字段级 + 行级 + 异步）
        // ============================================================
        public virtual string Error => ValidateRow(); // IDataErrorInfo 接口要求，行级错误放在 ValidateRow() 里
        /// <summary>
        /// 字段级验证（IDataErrorInfo 使用）
        /// 子类（如 SalesOrderLineModel）会 override 它。
        /// </summary>
        public virtual string this[string columnName] => null;

        /// <summary>
        /// 行级验证（跨字段验证）
        /// 默认无错误，子类可 override。
        /// </summary>
        public virtual string ValidateRow() => null;

        /// <summary>
        /// 异步验证（例如 SKU 是否存在数据库）
        /// 默认无错误，子类可 override。
        /// </summary>
        public virtual Task<string> ValidateAsync() =>
            Task.FromResult<string>(null);

        /// <summary>
        /// 是否有效（字段级 + 行级验证）
        /// 异步验证不包含在内，需要保存时单独调用。
        /// </summary>
        public virtual bool IsValid
        {
            get
            {
                // 字段级验证
                foreach (var prop in GetType().GetProperties())
                {
                    string error = this[prop.Name];
                    if (!string.IsNullOrEmpty(error))
                        return false;
                }

                // 行级验证
                if (!string.IsNullOrEmpty(ValidateRow()))
                    return false;

                return true;
            }
        }

        // 修复 4：增加一个强制触发整个 Model 验证的方法
        public void ForceValidationUI()
        {
            // string.Empty 会强制 WPF 重新读取该对象的所有绑定属性，触发全局验证
            RaisePropertyChanged(string.Empty);
            RaisePropertyChanged(nameof(IsValid));
            RaisePropertyChanged("Item[]"); // 确保强制触发 DataGrid 中的企业级验证
        }

        // ============================================================
        // 7. 撤销 / 提交
        // ============================================================

        public virtual void RollbackChanges()
        {
            using (DisabledStateScope())
            {
                // 1. 恢复所有被 SetProperty 修改过的属性
                foreach (var kv in _原始值字典)
                {
                    var prop = GetType().GetProperty(kv.Key);
                    if (prop != null)
                        prop.SetValue(this, kv.Value);
                }
            }

            _原始值字典.Clear();

            // 如果是从 Added 状态回滚，依然是 Added（除非你希望它消失）
            // 如果是从 Deleted 状态回滚，通常应该回到 Unchanged 或之前记录的状态
            State = ModelState.Unchanged;

            // 手动触发一次 IsChecked 的通知，确保全选逻辑正确
            RaisePropertyChanged(nameof(IsChecked));
        }

        public virtual void RestoreFromDeleted()
        {
            if (State == ModelState.Deleted)
            {
                // 恢复到 Modified 或 Unchanged，取决于它是否有原始 Uid
                State = Uid != Guid.Empty ? ModelState.Unchanged : ModelState.Added;

                // 如果需要保留用户在删除前填写的备注，就不调用 RollbackChanges
                // 仅仅是改变状态机
            }
        }

        /// <summary>提交修改：清理 ChangeTracker，重置状态机到 Unchanged。 </summary>
        public virtual void CommitChanges()
        {
            // 快速退出：已无改变且字典为空
            if (_state == ModelState.Unchanged && _原始值字典.Count == 0) return;

            // 抑制被 SetProperty 识别为“用户修改”
            using (DisabledStateScope())
            {
                // 直接设置 backing field 避免再次记录原始值 / 触发 MarkAsModified
                _state = ModelState.Unchanged;

                if (_原始值字典.Count > 0)
                    _原始值字典.Clear();
            }

            // 仅在需要时通知一次 State 变化
            RaisePropertyChanged(nameof(State));

        }
    }
}

