using System.Collections.Generic;
using System.ComponentModel;
using System.Dynamic;
using System.Runtime.CompilerServices;

namespace MateWpfThemes.FW.Mvvm
{
    /// <summary>
    /// 支持属性通知的动态对象，用于合计行
    /// </summary>
    public class NotifiableDynamicObject : DynamicObject, INotifyPropertyChanged
    {
        private readonly Dictionary<string, object> _properties = new Dictionary<string, object>();

        public event PropertyChangedEventHandler PropertyChanged;

        public override bool TryGetMember(GetMemberBinder binder, out object result)
        {
            return _properties.TryGetValue(binder.Name, out result);
        }

        public override bool TrySetMember(SetMemberBinder binder, object value)
        {
            SetProperty(binder.Name, value);
            return true;
        }

        public object this[string propertyName]
        {
            get => _properties.TryGetValue(propertyName, out var value) ? value : null;
            set => SetProperty(propertyName, value);
        }

        public void SetProperty(string propertyName, object value)
        {
            if (_properties.TryGetValue(propertyName, out var existingValue))
            {
                if (Equals(existingValue, value))
                    return;
            }

            _properties[propertyName] = value;
            OnPropertyChanged(propertyName);
        }

        public bool HasProperty(string propertyName)
        {
            return _properties.ContainsKey(propertyName);
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void RefreshAll()
        {
            // 通知所有属性已更改
            foreach (var key in _properties.Keys)
            {
                OnPropertyChanged(key);
            }
        }
    }

    /// <summary>
    /// 保持向后兼容性
    /// </summary>
    public class SummaryRow
    {
        public decimal Qty { get; set; }
        public decimal Amount { get; set; }
    }
}