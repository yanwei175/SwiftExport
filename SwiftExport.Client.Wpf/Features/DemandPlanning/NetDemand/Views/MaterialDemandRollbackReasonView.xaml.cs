using System.Windows.Controls;

namespace SwiftExportClient.Features.DemandPlanning.NetDemand.Views;

/// <summary>承载整修订回退说明及真实原因输入。</summary>
public partial class MaterialDemandRollbackReasonView : UserControl
{
    /// <summary>初始化原因输入视图，状态和校验由内容模型负责。</summary>
    public MaterialDemandRollbackReasonView() => InitializeComponent();
}
