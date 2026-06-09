using System.ComponentModel;

namespace SwiftExport.View.FW.Mvvm
{

    public class OptionItem<T>
    {
        public T Value { get; set; }
        public string Label { get; set; } = string.Empty;

        public override string ToString() => Label;
    }

    /// <summary>
    /// 通用多选选项包装类，用于多选下拉框（ToggleButton + Popup + CheckBox）的 CheckBox 绑定。
    /// T 通常为 int（枚举值）或 string，Label 是显示给用户的文本。
    /// IsSelected 实现 INotifyPropertyChanged，CheckBox.IsChecked 双向绑定后会即时刷新摘要文字。
    /// </summary>
    public class CheckableItem<T> : INotifyPropertyChanged
    {
        /// <summary>业务值，写入 SearchDto 时使用。</summary>
        public T Value { get; set; }

        /// <summary>显示给用户的标签文本。</summary>
        public string Label { get; set; }

        private bool _isSelected;
        /// <summary>是否被勾选，双向绑定到 CheckBox.IsChecked。</summary>
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>重写 ToString，使 Content="{Binding}" 时直接显示 Label。</summary>
        public override string ToString() => Label;
    }
}
