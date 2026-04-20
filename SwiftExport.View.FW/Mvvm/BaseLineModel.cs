using Mapster;
using SwiftExport.Contracts.DTOs.Base;
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
        public abstract void LoadFromDto<Tdto>(Tdto dto, bool 抑制清理脏数据 = false);//加载数据方法

        // ============================================================
        // 1. IBaseModel 基础字段
        // ============================================================

        /// <summary>
        /// 行主键（数据库 ID）
        /// </summary>
        public int Id { get; set; }

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
                using (程序内部修改Scope())
                {
                    SetProperty(ref _isChecked, value);
                }
            }
        }



        // ============================================================
        // 2. 程序内部修改抑制器（用于计算属性）
        // ============================================================

        /// <summary>
        /// true = 程序内部修改（不触发 Modified）
        /// false = 用户修改（触发 Modified）
        /// </summary>
        public bool 是否程序内部修改 { get; private set; }

        /// <summary>
        /// 使用 using(程序内部修改Scope()) 包裹计算逻辑，
        /// 这样计算属性不会触发 Modified。
        /// </summary>
        protected IDisposable 程序内部修改Scope()
        {
            是否程序内部修改 = true;
            return new ActionOnDispose(() => 是否程序内部修改 = false);
        }

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
            if (!是否程序内部修改)
                MarkAsModified();

            return true;
        }


        // ============================================================
        // 5. 状态机
        // ============================================================
        protected virtual void MarkAsModified()
        {
            if (State == ModelState.Unchanged)
                State = ModelState.Modified;
        }

        public void MarkAsAdded() => State = ModelState.Added;
        public void MarkAsDeleted() => State = ModelState.Deleted;


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
            using (程序内部修改Scope())
            {
                // 恢复原始值
                foreach (var kv in _原始值字典)
                {
                    var prop = GetType().GetProperty(kv.Key);
                    if (prop != null)
                        prop.SetValue(this, kv.Value);
                }
            }

            _原始值字典.Clear();
            State = ModelState.Unchanged;
        }

        public virtual void CommitChanges()
        {   // 清理原始值快照
            _原始值字典.Clear();
            // 状态重置
            State = ModelState.Unchanged;

        }
    }
}

