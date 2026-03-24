using SwiftExport.View.FW.Mvvm;

namespace SwiftExport.View.FW.ProductViews.Models
{

    public class ProductColorModel : ViewModelBase, IBaseModel
    {
        public int Id { get; set; }

        private ModelState _state = ModelState.Unchanged;
        public ModelState State
        {
            get => _state;
            set { _state = value; OnPropertyChanged(nameof(State)); }
        }

        private string _colorCodeUnique;
        public string ColorCodeUnique
        {
            get => _colorCodeUnique;
            set
            {
                if (_colorCodeUnique != value)
                {
                    _colorCodeUnique = value;
                    MarkAsModified();
                    OnPropertyChanged(nameof(ColorCodeUnique));
                }
            }
        }

        private string _colorCode;
        public string ColorCode
        {
            get => _colorCode;
            set { if (_colorCode != value) { _colorCode = value; MarkAsModified(); OnPropertyChanged(nameof(ColorCode)); } }
        }

        private string _colorNameEn;
        public string ColorNameEn
        {
            get => _colorNameEn;
            set { if (_colorNameEn != value) { _colorNameEn = value; MarkAsModified(); OnPropertyChanged(nameof(ColorNameEn)); } }
        }

        private string _colorNameZh;
        public string ColorNameZh
        {
            get => _colorNameZh;
            set { if (_colorNameZh != value) { _colorNameZh = value; MarkAsModified(); OnPropertyChanged(nameof(ColorNameZh)); } }
        }

        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                _isChecked = value;
                OnPropertyChanged(nameof(IsChecked));
            }
        }


        public bool IsValid
        {
            get
            {
                // 如果所有必填项都没有错误信息，则代表合法
                return !string.IsNullOrWhiteSpace(ColorCodeUnique) &&
                                       !string.IsNullOrWhiteSpace(ColorCode);
            }
        }

        private void MarkAsModified()
        {
            if (State == ModelState.Unchanged)
            {
                State = ModelState.Modified;
            }
        }
    }
}