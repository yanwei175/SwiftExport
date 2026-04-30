using Mapster;
using SwiftExport.View.FW.Mvvm;
using System.ComponentModel;
using System.Threading.Tasks;
namespace SwiftExport.View.FW.SystemViews.Models
{
    public class SysViewRouteModel : BaseLineModel, IDataErrorInfo
    {
        public override void LoadFromDto<Tdto>(Tdto dto, bool 抑制清理脏数据 = false)
        {
            if (dto == null) return;

            using (程序内部修改Scope())
            {
                // Mapster 将 DTO 属性值映射到 this
                dto.Adapt(this);

                if (this.Id == 0)
                {
                    this.MarkAsAdded(); // 新行标记为 Added 状态
                }

                // 根据调用方决定是否立即清理脏数据
                if (!抑制清理脏数据)
                    CommitChanges();
            }
        }

        #region 字段属性

        private string _targetViewKey;
        /// <summary>路由键，唯一标识一条路由</summary>
        public string TargetViewKey
        {
            get => _targetViewKey;
            set => SetProperty(ref _targetViewKey, value);
        }

        private string _viewModelTypeName;
        /// <summary>ViewModel 完整类型名（含命名空间）</summary>
        public string ViewModelTypeName
        {
            get => _viewModelTypeName;
            set => SetProperty(ref _viewModelTypeName, value);
        }

        private string _viewTypeName;
        /// <summary>
        ///— View 完整类型名（可选）。
        ///— 为空时由 ViewLocatorSelector 按约定自动推断。
        /// </summary>
        public string ViewTypeName
        {
            get => _viewTypeName;
            set => SetProperty(ref _viewTypeName, value);
        }

        private string _displayName;
        /// <summary>路由可读名称</summary>
        public string DisplayName
        {
            get => _displayName;
            set => SetProperty(ref _displayName, value);
        }

        private bool _isEnabled;
        /// <summary>是否启用</summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        private int _sortOrder;
        /// <summary>排序号</summary>
        public int SortOrder
        {
            get => _sortOrder;
            set => SetProperty(ref _sortOrder, value);
        }

        private string _routeCategory;
        /// <summary>路由分类：Dict / Standalone 等</summary>
        public string RouteCategory
        {
            get => _routeCategory;
            set => SetProperty(ref _routeCategory, value);
        }

        private string _assemblyName;
        /// <summary>程序集名称（多程序集扩展用，通常为空）</summary>
        public string AssemblyName
        {
            get => _assemblyName;
            set => SetProperty(ref _assemblyName, value);
        }

        private string _remark;
        /// <summary>SubNote</summary>
        public string Remark
        {
            get => _remark;
            set => SetProperty(ref _remark, value);
        }

        #endregion



        public override string Error => null;

        public override string this[string columnName]
        {
            get
            {
                if (columnName == nameof(TargetViewKey) && string.IsNullOrWhiteSpace(TargetViewKey))
                    return "路由键不能为空";
                if (columnName == nameof(DisplayName) && string.IsNullOrWhiteSpace(DisplayName))
                    return "路由可读名称不能为空";
                if (columnName == nameof(ViewModelTypeName) && string.IsNullOrWhiteSpace(ViewModelTypeName))
                    return "ViewModel 类型名不能为空";
                return null;
            }
        }

        public override string ValidateRow()
        {
            return null;
        }

        public override async Task<string> ValidateAsync()
        {
            return await Task.FromResult<string>(null);
        }

    }
}
