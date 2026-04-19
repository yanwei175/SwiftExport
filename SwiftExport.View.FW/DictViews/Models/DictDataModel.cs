using SwiftExport.View.FW.Mvvm;

namespace SwiftExport.View.FW.DictViews.Models
{
    // 2. 右侧的具体枚举项 (Detail)
    public class DictDataModel : BaseLineModel, IBaseModel
    {
        public override void LoadFromDto<Tdto>(Tdto dto, bool 抑制清理脏数据 = false)
        {
            throw new System.NotImplementedException();
        }

        #region 字段属性
        private string _dictCode;
        public string DictCode { get => _dictCode; set => SetProperty(ref _dictCode, value); }

        private string _dictLabel;
        public string DictLabel { get => _dictLabel; set => SetProperty(ref _dictLabel, value); }

        private string _dictValue;
        public string DictValue { get => _dictValue; set => SetProperty(ref _dictValue, value); }

        private int _sortOrder;
        public int SortOrder { get => _sortOrder; set => SetProperty(ref _sortOrder, value); }

        private bool _isSystem;
        public bool IsSystem { get => _isSystem; set => SetProperty(ref _isSystem, value); }
        #endregion

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

                if (columnName == nameof(DictLabel))
                {
                    if (string.IsNullOrWhiteSpace(DictLabel))
                        result = "字典标签不能为空";
                }

                if (columnName == nameof(DictValue))
                {
                    if (string.IsNullOrWhiteSpace(DictValue))
                        result = "字典值不能为空";
                }
                return result;
            }
        }




    }
}
