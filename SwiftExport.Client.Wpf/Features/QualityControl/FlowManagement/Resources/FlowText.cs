namespace SwiftExportClient.Features.QualityControl.FlowManagement.Resources;

/// <summary>流量页面共享单位和测量语义文案，由本 Feature 拥有。</summary>
public static class FlowText
{
    /// <summary>bar 压力列的固定流量单位。</summary>
    public const string BarUnit = "bar 压力列：L/min";
    /// <summary>PSI 压力列的固定流量单位。</summary>
    public const string PsiUnit = "PSI 压力列：GPM（美国加仑/分钟）";
    /// <summary>两套数据的输入规则，不暗示任何自动换算。</summary>
    public const string MeasurementHelp = "空格表示未测，0 表示实测零值；可填写任一套或两套压力列，不自动换算。";
}
