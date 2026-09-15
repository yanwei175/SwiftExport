using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExportClient.Features.Inventory.Outbounds.Services;
using SwiftExportClient.Features.Inventory.Outbounds.ViewModels;
using SwiftExportClient.Features.Inventory.Outbounds.Views;
using SwiftExportClient.Services.UI;

namespace SwiftExport.Client.Wpf.Tests.Features;

/// <summary>手工数量及保存前补填原因的交互回归，不连接服务端或数据库。</summary>
public sealed partial class OutboundManagerPersistenceTests
{
    /// <summary>保存补填的原因进入命令；已有原因不弹窗且不覆盖。</summary>
    [TestMethod]
    public async Task Save_CollectsMissingReasonWithoutOverwritingExistingReason()
    {
        using var f = new Fixture();
        f.Add(); var existing = f.Add(); existing.IssueReason = "生产领用";
        await f.Vm.SaveCommand.ExecuteAsync(null);
        Assert.AreEqual(1, f.IssueReasonDialogs.Calls);
        Assert.AreEqual("样品领用", f.Service.Creates[0].Draft.IssueReason);
        Assert.AreEqual("生产领用", f.Service.Creates[1].Draft.IssueReason);
        Assert.IsFalse(f.Vm.HasUnsavedChanges);
    }

    /// <summary>第二张取消时不提交第一张，保留已确认原因与全部未保存编辑。</summary>
    [TestMethod]
    public async Task Save_ReasonCancellationPreservesAllDrafts()
    {
        using var f = new Fixture(); var first = f.Add(); var second = f.Add();
        f.IssueReasonDialogs.CancelAt = 2;
        await f.Vm.SaveCommand.ExecuteAsync(null);
        Assert.AreEqual(0, f.Service.Creates.Count);
        Assert.AreEqual("样品领用", first.IssueReason);
        Assert.IsTrue(string.IsNullOrWhiteSpace(second.IssueReason));
        Assert.IsTrue(first.IsDirty); Assert.IsTrue(second.IsDirty);
    }

    /// <summary>原因等待期间不能重复保存；页面释放后迟到的确认不写入或回填。</summary>
    [TestMethod]
    public async Task Save_ReasonDialogGuardsRepeatedClickAndLateResult()
    {
        using var f = new Fixture(); var row = f.Add();
        f.IssueReasonDialogs.Wait = new();
        var pending = f.Vm.SaveCommand.ExecuteAsync(null);
        await f.Vm.SaveCommand.ExecuteAsync(null);
        Assert.AreEqual(1, f.IssueReasonDialogs.Calls);
        Assert.IsFalse(await f.Vm.CanCloseAsync(CancellationToken.None));
        f.Vm.Dispose(); f.IssueReasonDialogs.Wait.SetResult(); await pending;
        Assert.AreEqual(0, f.Service.Creates.Count);
        Assert.IsTrue(string.IsNullOrWhiteSpace(row.IssueReason));
    }

    /// <summary>真实表单绑定原因，空白和501字不能确认，500字可确认，关闭始终允许。</summary>
    [TestMethod]
    public void IssueReasonDialog_ValidatesAndBindsInput()
    {
        TestInfrastructure.StaTest.RunAsync(async () =>
        {
            var model = new OutboundIssueReasonViewModel("出库草稿");
            var view = new OutboundIssueReasonView { DataContext = model };
            await view.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
            var box = (System.Windows.Controls.TextBox)view.FindName("ReasonBox");
            Assert.IsFalse(model.IsPrimaryButtonEnabled);
            box.SetCurrentValue(System.Windows.Controls.TextBox.TextProperty, "样品领用");
            box.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)!.UpdateSource();
            Assert.AreEqual("样品领用", model.Reason);
            Assert.IsTrue(model.CanClose(DialogResult.Primary));
            model.Reason = " \t\n"; Assert.IsFalse(model.IsPrimaryButtonEnabled);
            model.Reason = new string('因', 500); Assert.IsTrue(model.IsPrimaryButtonEnabled);
            model.Reason = new string('因', 501); Assert.IsFalse(model.IsPrimaryButtonEnabled);
            Assert.IsTrue(model.CanClose(DialogResult.Close));
        });
    }

    /// <summary>模拟补填、取消和迟到确认结果，不持有业务页面。</summary>
    private sealed class IssueReasonDialogStub : IOutboundIssueReasonDialogService
    {
        public int Calls, CancelAt;
        public TaskCompletionSource? Wait;
        /// <inheritdoc/>
        public async Task<(DialogResult Result, string Reason)> CollectAsync(string documentNo, CancellationToken token)
        {
            Calls++;
            if (Wait is not null) await Wait.Task;
            return (Calls == CancelAt ? DialogResult.Close : DialogResult.Primary, "样品领用");
        }
    }
}
