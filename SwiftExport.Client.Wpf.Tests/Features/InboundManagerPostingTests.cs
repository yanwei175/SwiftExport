using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Posting;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Inventory.Inbounds.Services;

namespace SwiftExport.Client.Wpf.Tests.Features;

/// <summary>验证当前单预览、确认、过账幂等恢复及只回填当前单。</summary>
public sealed partial class InboundManagerPersistenceTests
{
    /// <summary>真实预览视图只读绑定行和库存影响，阻断项同时禁用确认与确认关闭。</summary>
    [TestMethod]
    public void PostPreviewView_BindsReadOnlyAndBlocksConfirmation()
    {
        TestInfrastructure.StaTest.RunAsync(async () =>
        {
            var preview = new InboundPostPreviewDto { CanPost = true };
            var model = new SwiftExportClient.Features.Inventory.Inbounds.ViewModels.InboundPostPreviewViewModel(preview);
            var view = new SwiftExportClient.Features.Inventory.Inbounds.Views.InboundPostPreviewView { DataContext = model };
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Loaded);
            var grids = ((System.Windows.Controls.StackPanel)view.Content).Children.OfType<System.Windows.Controls.DataGrid>().ToArray();
            Assert.AreEqual(2, grids.Length);
            foreach (var grid in grids)
            {
                grid.GetBindingExpression(System.Windows.Controls.ItemsControl.ItemsSourceProperty)!.UpdateTarget();
                Assert.IsTrue(grid.IsReadOnly);
            }
            Assert.AreSame(preview.Lines, grids[0].ItemsSource);
            Assert.AreSame(preview.StockImpacts, grids[1].ItemsSource);
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
        Assert.AreEqual(InboundPostingStatusEnum.Posted, row.PostingStatus);
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
        Assert.AreEqual(InboundPostingStatusEnum.Posted, row.PostingStatus);
        Assert.IsTrue(row.IsPersistencePending);
        f.Service.FailDetails = false;
        await f.Vm.RefreshCommand.ExecuteAsync(null);
        Assert.AreEqual(1, f.Service.Posts.Count);
        Assert.IsFalse(row.IsPersistencePending);
    }

    private sealed class PostDialogStub : IInboundPostDialogService
    {
        public bool Allow = true;
        public Task<bool> ConfirmAsync(InboundPostPreviewDto preview, CancellationToken token) => Task.FromResult(Allow);
    }

    private sealed partial class PersistenceStub
    {
        public int Previews, PostWrites;
        public bool BlockPreview, LosePostResponse;
        public List<PostInboundCommand> Posts { get; } = [];
        private readonly Dictionary<Guid, InboundPostResult> _postCommands = [];
        /// <inheritdoc/>
        public Task<ServiceResult<InboundPostPreviewDto>> PreviewPostAsync(Guid uid, InboundPostPreviewRequest request, CancellationToken token)
        {
            Previews++;
            return Task.FromResult(ServiceResult<InboundPostPreviewDto>.Ok(new()
            { Uid = uid, Version = request.ExpectedVersion, CanPost = !BlockPreview }));
        }
        /// <inheritdoc/>
        public Task<ServiceResult<InboundPostResult>> PostAsync(Guid uid, PostInboundCommand command, CancellationToken token)
        {
            Posts.Add(command);
            if (!_postCommands.TryGetValue(command.CommandId, out var result))
            {
                PostWrites++;
                var detail = _details[uid];
                detail.Header.PostingStatus = InboundPostingStatusEnum.Posted;
                detail.Header.Version++;
                result = new() { Uid = uid, DocumentNo = detail.Header.DocumentNo, Version = detail.Header.Version, PostingStatus = InboundPostingStatusEnum.Posted };
                _postCommands.Add(command.CommandId, result);
            }
            if (LosePostResponse)
            {
                LosePostResponse = false;
                return Task.FromResult(ServiceResult<InboundPostResult>.Fail(ServiceErrorCodes.OutcomeUnknown, "模拟响应丢失"));
            }
            return Task.FromResult(ServiceResult<InboundPostResult>.Ok(result));
        }
    }
}
