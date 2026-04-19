using SwiftExport.View.FW.Mvvm;
using System.Threading.Tasks;

namespace SwiftExport.View.FW.DictViews.Models
{
    // 中文注释：字典类型模型（用于字典类型管理窗体）
    public class DictTypeModel : BaseLineModel, IBaseModel
    {
        public override void LoadFromDto<Tdto>(Tdto dto, bool 抑制清理脏数据 = false)
        {
            throw new System.NotImplementedException();
        }

        #region 字段属性
        private string _dictCode;
        public string DictCode { get => _dictCode; set => SetProperty(ref _dictCode, value); }

        private string _dictName;
        public string DictName { get => _dictName; set => SetProperty(ref _dictName, value); }

        private bool _isSystem;
        public bool IsSystem { get => _isSystem; set => SetProperty(ref _isSystem, value); }

        private string _remark;
        public string Remark { get => _remark; set => SetProperty(ref _remark, value); }
        #endregion


        // ============================================================
        // 验证（IDataErrorInfo）用于字段级验证，适合验证单个字段的有效性，例如：必填项、格式验证等。
        // ============================================================
        #region  字段级验证IDataErrorInfo Members

        public override string Error => null;// 告诉 WPF：行级别没有整体错误，不要动整行的样式,必须返回 null，避免整行标红。如果要跨行验证，请使用 ValidateRow() 方法。

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


    }
}
