using System.Globalization;
using System.Windows.Data;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.QualityControl.FlowManagement.Converters;

/// <summary>把公共行状态显示为可读文本，不只依靠颜色表达修改。</summary>
public sealed class FlowLineStateConverter : IValueConverter
{
    /// <summary>显示新增、修改和已保存状态。</summary>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        LineState.Added => "新增", LineState.Modified => "修改", LineState.Deleted => "删除", _ => "已保存"
    };
    /// <summary>状态是只读显示，不允许反向写入。</summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
