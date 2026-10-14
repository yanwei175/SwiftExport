using System.Windows.Controls;

namespace SwiftExportClient.Features.SystemSettings.Dictionaries.Views;

/// <summary>指定类型的独立维护页复用字典明细组件，生命周期由文档导航拥有。</summary>
public partial class DictionaryDataManagerView : UserControl
{
    /// <summary>创建公共字典明细页面，模型和类型由文档导航初始化。</summary>
    public DictionaryDataManagerView() => InitializeComponent();
}
