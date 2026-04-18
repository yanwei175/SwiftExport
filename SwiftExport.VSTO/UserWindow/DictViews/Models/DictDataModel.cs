using SwiftExport.VSTO.UserWindow.Mvvm;

namespace SwiftExport.VSTO.UserWindow.DictViews.Models
{
    // 2. 右侧的具体枚举项 (Detail)
    public class DictDataModel : ViewModelBase, IBaseModel
    {
        public int Id { get; set; }

        private ModelState _state = ModelState.Unchanged;
        public ModelState State { get => _state; set { _state = value; OnPropertyChanged(); } }

        public bool IsChecked { get; set; }

        private string _dictCode;
        public string DictCode { get => _dictCode; set { _dictCode = value; MarkAsModified(); OnPropertyChanged(); } }

        private string _dictLabel;
        public string DictLabel { get => _dictLabel; set { _dictLabel = value; MarkAsModified(); OnPropertyChanged(); } }

        private string _dictValue;
        public string DictValue { get => _dictValue; set { _dictValue = value; MarkAsModified(); OnPropertyChanged(); } }

        private int _sortOrder;
        public int SortOrder { get => _sortOrder; set { _sortOrder = value; MarkAsModified(); OnPropertyChanged(); } }

        private bool _isSystem;
        public bool IsSystem { get => _isSystem; set { _isSystem = value; MarkAsModified(); OnPropertyChanged(); } }

        // 极其纯粹的保存前校验
        public bool IsValid => !string.IsNullOrWhiteSpace(DictLabel) && !string.IsNullOrWhiteSpace(DictValue);

        private void MarkAsModified()
        {
            if (State == ModelState.Unchanged) State = ModelState.Modified;
        }
    }
}
