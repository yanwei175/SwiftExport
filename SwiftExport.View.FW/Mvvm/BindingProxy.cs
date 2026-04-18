using System.Windows;

namespace SwiftExport.View.FW.Mvvm
{
    /// <summary>
    /// 中文注释：用于在 DataGridColumn 等不在可视树中的对象上做数据绑定中转。
    /// 典型场景：列可见性绑定到窗口 DataContext 的某个属性。
    /// </summary>
    public class BindingProxy : Freezable
    {
        protected override Freezable CreateInstanceCore()
        {
            return new BindingProxy();
        }

        /// <summary>
        /// 中文注释：中转数据对象。
        /// </summary>
        public object Data
        {
            get => GetValue(DataProperty);
            set => SetValue(DataProperty, value);
        }

        public static readonly DependencyProperty DataProperty =
            DependencyProperty.Register(nameof(Data), typeof(object), typeof(BindingProxy), new PropertyMetadata(null));
    }
}
