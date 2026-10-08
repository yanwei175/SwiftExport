using System.Globalization;
using System.Windows.Controls;
using System.Windows.Markup;

namespace SwiftExportClient.Features.QualityControl.FlowManagement.Views;

/// <summary>流量列表视图；业务查询和导航由 ViewModel 承担。</summary>
public partial class FlowManagerView : UserControl
{
    /// <summary>初始化视图，日期展示和输入沿用当前客户端文化。</summary>
    public FlowManagerView()
    {
        InitializeComponent();
        Language = XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag);
    }
}
