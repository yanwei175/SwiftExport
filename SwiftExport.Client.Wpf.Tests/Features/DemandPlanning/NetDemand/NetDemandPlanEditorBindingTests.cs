using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Xml.Linq;

namespace SwiftExport.Client.Wpf.Tests.Features.DemandPlanning.NetDemand;

/// <summary>
/// 验证净需求计划抽屉的只读展示字段不会通过 WPF 默认双向绑定回写 ViewModel。
/// </summary>
[TestClass]
public sealed class NetDemandPlanEditorBindingTests
{
    /// <summary>
    /// 确保只读 SKU、角色和送达目标显式使用单向绑定，避免打开批量计划抽屉时抛出只读属性异常。
    /// </summary>
    [TestMethod]
    public void ReadOnlyDisplayBindings_ShouldExplicitlyUseOneWayMode()
    {
        var document = XDocument.Load(GetViewPath());
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var runs = document.Descendants(presentation + "Run").ToArray();
        AssertBindingUsesOneWay(runs, "Sku");
        AssertBindingUsesOneWay(runs, "DemandRoleText");

        var deliveryTarget = document.Descendants(presentation + "TextBox")
            .Single(element => ((string?)element.Attribute("Text"))?.Contains("DeliveryTargetSummary", StringComparison.Ordinal) == true);
        StringAssert.Contains((string?)deliveryTarget.Attribute("Text") ?? string.Empty, "Mode=OneWay");
        Assert.AreEqual("True", (string?)deliveryTarget.Attribute("IsReadOnly"));
    }

    /// <summary>
    /// 验证指定只读属性对应的 Run 文本绑定明确声明为单向绑定。
    /// </summary>
    private static void AssertBindingUsesOneWay(IEnumerable<XElement> runs, string propertyName)
    {
        var run = runs.Single(element => ((string?)element.Attribute("Text"))?.Contains(propertyName, StringComparison.Ordinal) == true);
        StringAssert.Contains((string?)run.Attribute("Text") ?? string.Empty, "Mode=OneWay");
    }

    /// <summary>
    /// 从测试输出目录向上定位仓库，并返回净需求计划抽屉 XAML 路径。
    /// </summary>
    private static string GetViewPath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "SwiftExport.Client.Wpf",
                "Features",
                "DemandPlanning",
                "NetDemand",
                "Views",
                "NetDemandPlanEditorView.xaml");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        Assert.Fail("无法定位净需求计划抽屉 XAML。");
        return string.Empty;
    }
}
