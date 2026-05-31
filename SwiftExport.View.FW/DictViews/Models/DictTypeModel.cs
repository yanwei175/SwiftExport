using SwiftExport.View.FW.Mvvm;
using Mapster;
using SwiftExport.Contracts.DTOs.SystemDtos;
using System.ComponentModel;
using System.Threading.Tasks;

namespace SwiftExport.View.FW.DictViews.Models
{
    /// <summary>
    /// 字典类型模型 - 对应 SysDictTypeDto
    /// 用于左侧树形结构和字典类型管理
    /// </summary>
    public class DictTypeModel : BaseLineModel, IDataErrorInfo
    {
        /// <summary>
        /// 从 DTO 加载数据到 Model
        /// </summary>
        public override void LoadFromDto<DictTypeModel>(DictTypeModel dto, bool 抑制清理脏数据 = false)
        {
            if (dto == null) return;

            using (DisabledStateScope())
            {
                dto.Adapt(this);  // 使用 Mapster 映射 DTO → Model
                if (this.Id == 0)
                {
                    this.MarkAsAdded();
                }

                if (!抑制清理脏数据)
                    CommitChanges();
            }
        }

        #region 字段属性

        /// <summary>
        /// 字典编码（如 "Sex"、"Status"）
        /// </summary>
        private string _dictCode;
        public string DictCode
        {
            get => _dictCode;
            set => SetProperty(ref _dictCode, value);
        }

        /// <summary>
        /// 字典名称（如 "性别"、"状态"）
        /// </summary>
        private string _dictName;
        public string DictName
        {
            get => _dictName;
            set => SetProperty(ref _dictName, value);
        }

        /// <summary>
        /// 是否系统字典（系统字典不允许删除）
        /// </summary>
        private bool _isSystem;
        public bool IsSystem
        {
            get => _isSystem;
            set => SetProperty(ref _isSystem, value);
        }

        /// <summary>
        /// 备注说明
        /// </summary>
        private string _remark;
        public string Remark
        {
            get => _remark;
            set => SetProperty(ref _remark, value);
        }

        /// <summary>
        /// 父级字典 ID（支撑树形结构，用于分层字典）
        /// </summary>
        private int? _parentId;
        public int? ParentId
        {
            get => _parentId;
            set => SetProperty(ref _parentId, value);
        }

        /// <summary>
        /// 排序编号（左侧树的排序）
        /// </summary>
        private int _sortOrder;
        public int SortOrder
        {
            get => _sortOrder;
            set => SetProperty(ref _sortOrder, value);
        }

        /// <summary>
        /// 目标视图键（告诉前端右侧应该加载哪个 View）
        /// 如 "DictDataView"、"SupplyTypeView"
        /// </summary>
        private string _targetViewKey;
        public string TargetViewKey
        {
            get => _targetViewKey;
            set => SetProperty(ref _targetViewKey, value);
        }

        /// <summary>
        /// 分组代码（用于逻辑分组，如 "Basic"、"Finance"、"Sales"）
        /// </summary>
        private string _groupCode;
        public string GroupCode
        {
            get => _groupCode;
            set => SetProperty(ref _groupCode, value);
        }

        /// <summary>
        /// 分组名称（如 "基础数据"、"财务"、"销售"）
        /// </summary>
        private string _groupName;
        public string GroupName
        {
            get => _groupName;
            set => SetProperty(ref _groupName, value);
        }

        #endregion

        #region IDataErrorInfo Members

        /// <summary>
        /// 行级错误（返回 null 避免整行标红）
        /// </summary>
        public override string Error => null;

        /// <summary>
        /// 字段级验证
        /// </summary>
        public override string this[string columnName]
        {
            get
            {
                string result = null;

                if (columnName == nameof(DictCode))
                {
                    if (string.IsNullOrWhiteSpace(DictCode))
                        result = "字典编码不能为空";
                }

                if (columnName == nameof(DictName))
                {
                    if (string.IsNullOrWhiteSpace(DictName))
                        result = "字典名称不能为空";
                }

                return result;
            }
        }

        #endregion

        #region 行级/异步验证

        /// <summary>
        /// 行级验证（跨字段验证）
        /// </summary>
        public override string ValidateRow()
        {
            // 可扩展：检查编码唯一性等跨字段逻辑
            return null;
        }

        /// <summary>
        /// 异步验证（数据库验证）
        /// </summary>
        public override async Task<string> ValidateAsync()
        {
            // 可扩展：检查编码是否已存在等
            return null;
        }

        #endregion
    }
}
