using SwiftExport.View.FW.Mvvm;

namespace SwiftExport.View.FW.DictViews.Models
{
    // 中文注释：字典类型模型（用于字典类型管理窗体）
    public class DictTypeModel : ViewModelBaseEx, IBaseModel
    {
        public int Id { get; set; }

        private ModelState _state = ModelState.Unchanged;
        public ModelState State { get => _state; set { _state = value; RaisePropertyChanged(); } }

        public bool IsChecked { get; set; }

        private string _dictCode;
        public string DictCode { get => _dictCode; set { _dictCode = value; MarkAsModified(); RaisePropertyChanged(); } }

        private string _dictName;
        public string DictName { get => _dictName; set { _dictName = value; MarkAsModified(); RaisePropertyChanged(); } }

        private bool _isSystem;
        public bool IsSystem { get => _isSystem; set { _isSystem = value; MarkAsModified(); RaisePropertyChanged(); } }

        private string _remark;
        public string Remark { get => _remark; set { _remark = value; MarkAsModified(); RaisePropertyChanged(); } }

        // 中文注释：保存前基础校验（类型编码和类型名称必填）
        public bool IsValid => !string.IsNullOrWhiteSpace(DictCode) && !string.IsNullOrWhiteSpace(DictName);

        // 中文注释：仅在未改动状态时标记为修改
        private void MarkAsModified()
        {
            if (State == ModelState.Unchanged) State = ModelState.Modified;
        }
    }
}
