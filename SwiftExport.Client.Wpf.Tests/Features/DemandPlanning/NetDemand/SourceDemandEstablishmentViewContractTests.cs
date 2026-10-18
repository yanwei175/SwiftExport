using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;

namespace SwiftExport.Client.Wpf.Tests.Features.DemandPlanning.NetDemand;

/// <summary>验证从成品需求建立物料需求的入口只存在于物料需求页面。</summary>
[TestClass]
public sealed class SourceDemandEstablishmentViewContractTests
{
    [TestMethod]
    public void NetDemandPage_ShouldOwnCandidateEntryAndSourcePageShouldRemoveDuplicateCommands()
    {
        var netView = Read("SwiftExport.Client.Wpf", "Features", "DemandPlanning", "NetDemand", "Views", "NetDemandManagerView.xaml");
        var picker = Read("SwiftExport.Client.Wpf", "Features", "DemandPlanning", "NetDemand", "Views", "SourceDemandCandidatePickerView.xaml");
        var sourceView = Read("SwiftExport.Client.Wpf", "Features", "DemandPlanning", "GrossDemand", "Views", "GrossDemandWorkspaceView.xaml");

        StringAssert.Contains(netView, "EstablishFromSourcesCommand");
        StringAssert.Contains(netView, "RollbackSourceEstablishmentCommand");
        StringAssert.Contains(picker, "SourceGroups");
        StringAssert.Contains(picker, "SelectedSourceGroup");
        StringAssert.Contains(picker, "DataGridTemplateColumn");
        StringAssert.Contains(picker, "Demand.SourceNo");
        StringAssert.Contains(picker, "Demand.SourceLineNumber");
        StringAssert.Contains(picker, "Demand.LinkSku");
        StringAssert.Contains(picker, "Demand.ImagePath");
        StringAssert.Contains(picker, "EstablishSelectedCommand");
        Assert.IsFalse(picker.Contains("DataGridCheckBoxColumn", StringComparison.Ordinal));
        Assert.IsFalse(picker.Contains("Keyword", StringComparison.Ordinal));
        Assert.IsFalse(picker.Contains("SearchCommand", StringComparison.Ordinal));
        Assert.IsFalse(sourceView.Contains("DecomposeSelectedCommand", StringComparison.Ordinal));
        Assert.IsFalse(sourceView.Contains("RollbackSelectedCommand", StringComparison.Ordinal));
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray()));

    private static string Root()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) return directory.FullName;
        Assert.Fail("无法定位 SwiftExport 仓库根目录。");
        return string.Empty;
    }
}
