using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Inventory.Outbounds;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Inventory.Outbounds.Services;
using SwiftExportClient.Features.Inventory.Outbounds.ViewModels;
using SwiftExportClient.Features.Inventory.Outbounds.Views;
using SwiftExportClient.Services.UI;

namespace SwiftExport.Client.Wpf.Tests.Features;
/// <summary>整单反向的页面准入、原因、状态接纳及未知结果恢复。</summary>
public sealed partial class OutboundManagerPersistenceTests
{
    /// <summary>取消、权限或自动单不提交；成功后保留只读并禁止再次反向。</summary>
    [TestMethod]
    public async Task Reverse_GuardsAndRestoresReadOnly()
    {
        using var f = new Fixture(); var row = f.Add();
        Assert.IsFalse(f.Vm.ReverseCommand.CanExecute(null));
        await f.Vm.SaveCommand.ExecuteAsync(null); await f.Vm.PostCommand.ExecuteAsync(null);
        Assert.IsTrue(f.Vm.ReverseCommand.CanExecute(null));
        f.ReverseDialogs.Reason = null; await f.Vm.ReverseCommand.ExecuteAsync(null);
        Assert.AreEqual(0, f.Service.Reverses.Count);
        f.ReverseDialogs.Reason = "错误出库"; f.Service.BlockReverse = true;
        await f.Vm.ReverseCommand.ExecuteAsync(null); Assert.AreEqual(0, f.Service.Reverses.Count);
        f.Service.BlockReverse = false;
        using (row.SuppressChangeTracking()) row.TriggerPackingListId = Guid.NewGuid();
        Assert.IsFalse(f.Vm.ReverseCommand.CanExecute(null));
        using (row.SuppressChangeTracking()) row.TriggerPackingListId = null;
        await f.Vm.ReverseCommand.ExecuteAsync(null);
        Assert.AreEqual(OutboundPostingStatusEnum.Reversed, row.PostingStatus);
        Assert.IsFalse(row.CanEdit); Assert.IsFalse(f.Vm.ReverseCommand.CanExecute(null));
        Assert.IsFalse(f.Vm.PostCommand.CanExecute(null)); Assert.AreEqual("错误出库", row.ReverseReason);
        f.User.Clear(); Assert.IsFalse(f.Vm.ReverseCommand.CanExecute(null));
    }

    /// <summary>响应丢失复用原命令，成功后详情失败只读取，不影响另一草稿。</summary>
    [TestMethod]
    public async Task Reverse_UnknownAndDetailFailure_PreserveOriginalCommand()
    {
        using var f = new Fixture(); var row = f.Add();
        await f.Vm.SaveCommand.ExecuteAsync(null); await f.Vm.PostCommand.ExecuteAsync(null);
        var other = f.Add(); f.Vm.SelectedItem = row;
        f.Service.LoseReverseResponse = true;
        await f.Vm.ReverseCommand.ExecuteAsync(null);
        Assert.IsTrue(row.IsPersistencePending); Assert.IsFalse(await f.Vm.CanCloseAsync(CancellationToken.None));
        var original = f.Service.Reverses.Single(); f.Service.FailDetails = true;
        await f.Vm.RefreshCommand.ExecuteAsync(null);
        Assert.AreSame(original, f.Service.Reverses[1]); Assert.AreEqual(1, f.Service.ReverseWrites);
        Assert.AreEqual(OutboundPostingStatusEnum.Reversed, row.PostingStatus); Assert.IsFalse(row.CanEdit);
        Assert.AreEqual("错误出库", row.ReverseReason);
        f.Service.FailDetails = false; await f.Vm.RefreshCommand.ExecuteAsync(null);
        Assert.AreEqual(2, f.Service.Reverses.Count); Assert.IsFalse(row.IsPersistencePending); Assert.IsTrue(other.IsDirty);
    }

    /// <summary>等待预览期间切换或关闭不发命令；重复点击不产生第二次写入。</summary>
    [TestMethod]
    public async Task Reverse_LatePreviewAndRepeatedClick_DoNotDuplicate()
    {
        using var f = new Fixture(); var row = f.Add();
        await f.Vm.SaveCommand.ExecuteAsync(null); await f.Vm.PostCommand.ExecuteAsync(null);
        f.Service.ReversePreviewWait = new();
        var first = f.Vm.ReverseCommand.ExecuteAsync(null);
        await f.Vm.ReverseCommand.ExecuteAsync(null);
        Assert.AreEqual(1, f.Service.ReversePreviews);
        f.Vm.Dispose(); f.Service.ReversePreviewWait.SetResult(); await first;
        Assert.AreEqual(0, f.Service.Reverses.Count);
    }

    /// <summary>真实模板绑定原因，空白、超长和阻断信息禁用确认。</summary>
    [TestMethod]
    public void ReverseDialog_ValidatesAndBindsReason()
    {
        TestInfrastructure.StaTest.RunAsync(async () =>
        {
            var preview = new OutboundReversePreviewDto { CanReverse = true };
            var model = new OutboundReversePreviewViewModel(preview);
            Assert.IsFalse(model.IsPrimaryButtonEnabled);
            model.Reason = " \t\n"; Assert.IsFalse(model.IsPrimaryButtonEnabled);
            model.Reason = new string('因', 501); Assert.IsFalse(model.IsPrimaryButtonEnabled);
            model.Reason = new string('因', 500); Assert.IsTrue(model.IsPrimaryButtonEnabled);
            var view = new OutboundReversePreviewView { DataContext = model };
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.DataBind);
            var panel = (System.Windows.Controls.StackPanel)view.Content;
            var text = panel.Children.OfType<System.Windows.Controls.TextBox>().Single();
            text.SetCurrentValue(System.Windows.Controls.TextBox.TextProperty, "错误出库");
            text.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)!.UpdateSource();
            Assert.AreEqual("错误出库", model.Reason);
            Assert.IsTrue(panel.Children.OfType<System.Windows.Controls.DataGrid>().Single().IsReadOnly);
            preview.Blockers.Add("已有反向"); Assert.IsFalse(model.CanClose(DialogResult.Primary));
        });
    }

    private sealed class ReverseDialogStub : IOutboundReverseDialogService
    {
        public string? Reason = "错误出库";
        /// <inheritdoc/>
        public Task<string?> ConfirmAsync(OutboundReversePreviewDto preview, CancellationToken token) => Task.FromResult(Reason);
    }
    private sealed partial class PersistenceStub
    {
        public List<ReverseOutboundCommand> Reverses { get; } = [];
        public int ReverseWrites, ReversePreviews;
        public bool LoseReverseResponse, BlockReverse;
        public TaskCompletionSource? ReversePreviewWait;
        private readonly Dictionary<Guid, OutboundReverseResult> _reverseCommands = [];
        /// <inheritdoc/>
        public async Task<ServiceResult<OutboundReversePreviewDto>> PreviewReverseAsync(Guid uid, OutboundReversePreviewRequest request, CancellationToken token)
        {
            ReversePreviews++; if (ReversePreviewWait is not null) await ReversePreviewWait.Task;
            return ServiceResult<OutboundReversePreviewDto>.Ok(new() { Uid = uid, Version = request.ExpectedVersion, CanReverse = !BlockReverse });
        }
        /// <inheritdoc/>
        public Task<ServiceResult<OutboundReverseResult>> ReverseAsync(Guid uid, ReverseOutboundCommand command, CancellationToken token)
        {
            Reverses.Add(command);
            if (!_reverseCommands.TryGetValue(command.CommandId, out var result))
            {
                ReverseWrites++; var h = _details[uid].Header; h.Version++; h.PostingStatus = OutboundPostingStatusEnum.Reversed;
                h.ReversedDate = DateTime.Now; h.ReversedBy = "测试"; h.ReverseReason = command.Reason;
                result = new() { Uid = uid, Version = h.Version, PostingStatus = h.PostingStatus, ReversedDate = h.ReversedDate.Value,
                    ReversedBy = h.ReversedBy, ReverseReason = h.ReverseReason, DocumentNo = h.DocumentNo };
                _reverseCommands.Add(command.CommandId, result);
            }
            if (LoseReverseResponse) { LoseReverseResponse = false; return Task.FromResult(ServiceResult<OutboundReverseResult>.Fail(OutboundErrorCodes.OutcomeUnknown, "模拟响应丢失")); }
            return Task.FromResult(ServiceResult<OutboundReverseResult>.Ok(result));
        }
    }
}
