using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Inventory.Outbounds;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Inventory.Outbounds.Services;

namespace SwiftExport.Client.Wpf.Tests.Features;

/// <summary>验证当前单预览、确认、过账幂等恢复及只回填当前单。</summary>
public sealed partial class OutboundManagerPersistenceTests
{
    /// <summary>真实预览视图只读绑定行和库存影响，阻断项同时禁用确认与确认关闭。</summary>
    [TestMethod]
    public void PostPreviewView_BindsReadOnlyAndBlocksConfirmation()
    {
        TestInfrastructure.StaTest.RunAsync(async () =>
        {
            var preview = new OutboundPostPreviewDto { CanPost = true, IssueReason = "展厅样品领用" };
            var model = new SwiftExportClient.Features.Inventory.Outbounds.ViewModels.OutboundPostPreviewViewModel(preview);
            var view = new SwiftExportClient.Features.Inventory.Outbounds.Views.OutboundPostPreviewView { DataContext = model };
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Loaded);
            var reasonText = (System.Windows.Controls.TextBlock)view.FindName("IssueReasonText");
            reasonText.GetBindingExpression(System.Windows.Controls.TextBlock.TextProperty)!.UpdateTarget();
            StringAssert.Contains(reasonText.Text, "展厅样品领用");
            var grids = ((System.Windows.Controls.StackPanel)view.Content).Children.OfType<System.Windows.Controls.DataGrid>().ToArray();
            Assert.AreEqual(1, grids.Length);
            foreach (var grid in grids)
            {
                grid.GetBindingExpression(System.Windows.Controls.ItemsControl.ItemsSourceProperty)!.UpdateTarget();
                Assert.IsTrue(grid.IsReadOnly);
            }
            
            Assert.AreSame(preview.StockImpacts, grids[0].ItemsSource);
            Assert.IsTrue(model.IsPrimaryButtonEnabled);
            preview.Blockers.Add(new() { Message = "容量不足" });
            Assert.IsFalse(model.IsPrimaryButtonEnabled);
            Assert.IsFalse(model.CanClose(SwiftExportClient.Services.UI.DialogResult.Primary));
        });
    }

    /// <summary>取消、阻断及本地修改不发送库存命令，普通用户没有操作能力。</summary>
    [TestMethod]
    public async Task Post_GuardsDirtyPreviewAndPermissions()
    {
        using var f = new Fixture();
        var row = f.Add();
        await f.Vm.SaveCommand.ExecuteAsync(null);
        row.Lines.Items[0].Quantity = 3;
        await f.Vm.PostCommand.ExecuteAsync(null);
        Assert.AreEqual(0, f.Service.Previews);
        await f.Vm.SaveCommand.ExecuteAsync(null);
        f.PostDialogs.Allow = false;
        await f.Vm.PostCommand.ExecuteAsync(null);
        Assert.AreEqual(1, f.Service.Previews);
        Assert.AreEqual(0, f.Service.Posts.Count);
        f.PostDialogs.Allow = true; f.Service.BlockPreview = true;
        await f.Vm.PostCommand.ExecuteAsync(null);
        Assert.AreEqual(0, f.Service.Posts.Count);
        f.User.Clear();
        Assert.IsFalse(f.Vm.PostCommand.CanExecute(null));
    }

    /// <summary>过账响应丢失保留原命令，成功后其他草稿和筛选不被刷新覆盖。</summary>
    [TestMethod]
    public async Task Post_UnknownReplaysSameCommand_AndPreservesOtherDraft()
    {
        using var f = new Fixture();
        var row = f.Add();
        await f.Vm.SaveCommand.ExecuteAsync(null);
        var other = f.Add();
        f.Vm.SelectedItem = row;
        f.Service.LosePostResponse = true;
        await f.Vm.PostCommand.ExecuteAsync(null);
        Assert.IsTrue(row.IsPersistencePending);
        Assert.IsFalse(await f.Vm.CanCloseAsync(CancellationToken.None));
        var command = f.Service.Posts.Single();
        await f.Vm.RefreshCommand.ExecuteAsync(null);
        Assert.AreSame(command, f.Service.Posts[1]);
        Assert.AreEqual(1, f.Service.PostWrites);
        Assert.AreEqual(OutboundPostingStatusEnum.Posted, row.PostingStatus);
        Assert.IsFalse(row.CanEdit);
        Assert.IsTrue(other.IsDirty);
        Assert.AreEqual(2, f.Vm.Headers.Items.Count);
    }

    /// <summary>过账成功但详情失败时刷新只读取，不重发命令。</summary>
    [TestMethod]
    public async Task Post_AcceptedButDetailFailed_RetriesOnlyRead()
    {
        using var f = new Fixture();
        var row = f.Add();
        await f.Vm.SaveCommand.ExecuteAsync(null);
        f.Service.FailDetails = true;
        await f.Vm.PostCommand.ExecuteAsync(null);
        Assert.AreEqual(OutboundPostingStatusEnum.Posted, row.PostingStatus);
        Assert.IsTrue(row.IsPersistencePending);
        f.Service.FailDetails = false;
        await f.Vm.RefreshCommand.ExecuteAsync(null);
        Assert.AreEqual(1, f.Service.Posts.Count);
        Assert.IsFalse(row.IsPersistencePending);
    }

    private sealed class PostDialogStub : IOutboundPostDialogService
    {
        public bool Allow = true;
        public Task<bool> ConfirmAsync(OutboundPostPreviewDto preview, CancellationToken token) => Task.FromResult(Allow);
    }

    private sealed partial class PersistenceStub
    {
        public int Previews, PostWrites;
        public bool BlockPreview, LosePostResponse;
        public List<PostOutboundCommand> Posts { get; } = [];
        private readonly Dictionary<Guid, OutboundPostResult> _postCommands = [];
        /// <inheritdoc/>
        public Task<ServiceResult<OutboundPostPreviewDto>> PreviewPostAsync(Guid uid, OutboundPostPreviewRequest request, CancellationToken token)
        {
            Previews++;
            return Task.FromResult(ServiceResult<OutboundPostPreviewDto>.Ok(new()
            { Uid = uid, Version = request.ExpectedVersion, CanPost = !BlockPreview }));
        }
        /// <inheritdoc/>
        public Task<ServiceResult<OutboundPostResult>> PostAsync(Guid uid, PostOutboundCommand command, CancellationToken token)
        {
            Posts.Add(command);
            if (!_postCommands.TryGetValue(command.CommandId, out var result))
            {
                PostWrites++;
                var detail = _details[uid];
                detail.Header.PostingStatus = OutboundPostingStatusEnum.Posted;
                detail.Header.Version++;
                result = new() { Uid = uid, DocumentNo = detail.Header.DocumentNo, Version = detail.Header.Version, PostingStatus = OutboundPostingStatusEnum.Posted };
                _postCommands.Add(command.CommandId, result);
            }
            if (LosePostResponse)
            {
                LosePostResponse = false;
                return Task.FromResult(ServiceResult<OutboundPostResult>.Fail(OutboundErrorCodes.OutcomeUnknown, "模拟响应丢失"));
            }
            return Task.FromResult(ServiceResult<OutboundPostResult>.Ok(result));
        }
    }
}
