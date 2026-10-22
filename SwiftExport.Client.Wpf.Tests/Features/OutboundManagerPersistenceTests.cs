using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Inventory.Outbounds;
using SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Inventory.Outbounds.Models;
using SwiftExportClient.Features.Inventory.Outbounds.Services;
using SwiftExportClient.Features.Inventory.Outbounds.ViewModels;
using SwiftExportClient.Features.Inventory.WarehouseSites.Services;
using SwiftExportClient.Services.UI;
using SwiftExport.Contracts.Apis.Inventory;
using Microsoft.Extensions.Logging.Abstractions;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExportClient.Features.Inventory.Outbounds.Views;

namespace SwiftExport.Client.Wpf.Tests.Features;

/// <summary>验证 Manager 保存和软删除的可见行为，模拟服务端提交但响应丢失，不连接数据库。</summary>
[TestClass]
public sealed partial class OutboundManagerPersistenceTests
{
    /// <summary>真实删除表单将原因回写模型，空白与超长原因不会放行确认。</summary>
    [TestMethod]
    public void DeleteDialog_BindsReasonAndGuardsConfirmation()
    {
        StaTest.RunAsync(async () =>
        {
            var model = new OutboundDeleteDialogViewModel();
            var view = new OutboundDeleteDialogView { DataContext = model };
            await view.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
            var box = (System.Windows.Controls.TextBox)view.FindName("ReasonBox");
            box.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)!.UpdateTarget();
            Assert.IsFalse(model.IsPrimaryButtonEnabled);
            box.SetCurrentValue(System.Windows.Controls.TextBox.TextProperty, "录入错误");
            await view.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
            Assert.AreEqual("录入错误", model.Reason);
            Assert.IsTrue(model.IsPrimaryButtonEnabled);
            model.Reason = "   ";
            Assert.IsFalse(model.CanClose(DialogResult.Primary));
            model.Reason = new string('字', 501);
            Assert.IsFalse(model.CanClose(DialogResult.Primary));
            Assert.IsTrue(model.CanClose(DialogResult.Close));
        });
    }

    /// <summary>持久化边界保留业务错误码，网络失败和缺失错误码的异常响应不能假定未提交。</summary>
    [TestMethod]
    public async Task PersistenceService_ClassifiesUnknownAndStructuredFailures()
    {
        var api = new TransportStub();
        var service = new OutboundPersistenceService(api, NullLogger<OutboundPersistenceService>.Instance);
        var command = new CreateOutboundDraftCommand { CommandId = Guid.NewGuid() };
        api.Response = ServiceResult<OutboundDraftCommandResult>.Fail(ServiceErrorCodes.VersionConflict, "版本冲突", "ExpectedVersion");
        Assert.AreEqual(ServiceErrorCodes.VersionConflict, (await service.CreateAsync(command, CancellationToken.None)).Error.Code);
        api.Response = new ServiceResult<OutboundDraftCommandResult> { Success = false, Message = "响应缺少错误码" };
        Assert.AreEqual(OutboundErrorCodes.OutcomeUnknown, (await service.CreateAsync(command, CancellationToken.None)).Error.Code);
        api.Exception = new System.Net.Http.HttpRequestException("模拟网络中断");
        Assert.AreEqual(OutboundErrorCodes.OutcomeUnknown, (await service.CreateAsync(command, CancellationToken.None)).Error.Code);
        api.Exception = new TaskCanceledException();
        Assert.AreEqual(OutboundErrorCodes.OutcomeUnknown, (await service.CreateAsync(command, CancellationToken.None)).Error.Code);
    }

    /// <summary>完整新增取得服务器身份；更新保留旧行身份、发送新行空身份并省略删除行。</summary>
    [TestMethod]
    public async Task Save_CreateAndUpdate_UsesStableLineIdentity()
    {
        using var f = new Fixture();
        var row = f.Add();
        await f.Vm.SaveCommand.ExecuteAsync(null);
        Assert.AreEqual(Guid.Empty, f.Service.Creates[0].Draft.Lines[0].Uid);
        Assert.IsFalse(row.IsNew);
        Assert.IsFalse(f.Vm.HasUnsavedChanges);
        var originalId = row.Lines.Items[0].Uid;
        row.Lines.Items[0].Quantity = 4;
        f.AddLine(row);
        await f.Vm.SaveCommand.ExecuteAsync(null);
        Assert.AreEqual(originalId, f.Service.Updates[0].Draft.Lines[0].Uid);
        Assert.AreEqual(Guid.Empty, f.Service.Updates[0].Draft.Lines[1].Uid);
        Assert.AreEqual(1, f.Service.Updates[0].ExpectedVersion);
        row.Lines.Remove(row.Lines.Items[0]);
        await f.Vm.SaveCommand.ExecuteAsync(null);
        Assert.AreEqual(1, f.Service.Updates[1].Draft.Lines.Count);
        Assert.AreNotEqual(originalId, f.Service.Updates[1].Draft.Lines[0].Uid);
        Assert.IsFalse(f.Vm.HasUnsavedChanges);
    }

    /// <summary>所有修改单据预校验完成前不提交；数量大于旧可收量仍是合法草稿。</summary>
    [TestMethod]
    public async Task Save_ValidatesAllRowsBeforeAnyRequest()
    {
        using var f = new Fixture();
        f.Add();
        var invalid = f.Add();
        invalid.Lines.Items[0].Quantity = 0;
        await f.Vm.SaveCommand.ExecuteAsync(null);
        Assert.AreEqual(0, f.Service.Creates.Count);
        Assert.AreSame(invalid, f.Vm.SelectedItem);
        invalid.Lines.Items[0].Quantity = null;
        await f.Vm.SaveCommand.ExecuteAsync(null);
        Assert.AreEqual(0, f.Service.Creates.Count);
        Assert.AreSame(invalid, f.Vm.SelectedItem);
        Assert.IsTrue(f.Vm.HasUnsavedChanges);
        invalid.Lines.Items[0].Quantity = 100;
        await f.Vm.SaveCommand.ExecuteAsync(null);
        Assert.AreEqual(2, f.Service.Writes);
        Assert.IsFalse(f.Vm.HasUnsavedChanges);
    }

    /// <summary>多单第二张失败时只接纳第一张，第三张不得发送。</summary>
    [TestMethod]
    public async Task Save_PartialSuccess_PreservesRemainingEdits()
    {
        using var f = new Fixture();
        var first = f.Add(); var second = f.Add(); var third = f.Add();
        f.Service.FailCreateAt = 2;
        await f.Vm.SaveCommand.ExecuteAsync(null);
        Assert.AreEqual(2, f.Service.Creates.Count);
        Assert.IsFalse(first.IsDirty);
        Assert.IsTrue(second.IsDirty);
        Assert.IsTrue(third.IsDirty);
        Assert.IsTrue(second.CanEdit);
        Assert.AreSame(second, f.Vm.SelectedItem);
    }

    /// <summary>已提交但响应未知时锁定原请求，权限拒绝不解除待确认；重试不会重复创建。</summary>
    [TestMethod]
    [DataRow("OUTCOME_UNKNOWN")]
    [DataRow("UNEXPECTED")]
    [DataRow("SERVICE_UNAVAILABLE")]
    public async Task UnknownResult_RetriesOriginalCommand_AndBlocksDiscard(string errorCode)
    {
        using var f = new Fixture();
        var row = f.Add();
        f.Service.LoseNextResponse = true;
        f.Service.LossErrorCode = errorCode;
        await f.Vm.SaveCommand.ExecuteAsync(null);
        Assert.IsFalse(row.CanEdit);
        Assert.IsTrue(row.IsPersistencePending);
        Assert.IsFalse(await f.Vm.CanCloseAsync(CancellationToken.None));
        await f.Vm.SaveCommand.ExecuteAsync(null);
        Assert.AreEqual(1, f.Service.Creates.Count);
        f.Service.DenyRequests = true;
        await f.Vm.RefreshCommand.ExecuteAsync(null);
        Assert.IsTrue(row.IsPersistencePending);
        f.Service.DenyRequests = false;
        await f.Vm.RefreshCommand.ExecuteAsync(null);
        Assert.AreSame(f.Service.Creates[0], f.Service.Creates[2]);
        Assert.AreEqual(1, f.Service.Writes);
        Assert.IsFalse(row.IsPersistencePending);
        Assert.IsFalse(f.Vm.HasUnsavedChanges);
    }

    /// <summary>写成功后 GET 失败只重试详情，绝不重发新建。</summary>
    [TestMethod]
    public async Task DetailFailureAfterSave_DoesNotRepeatWrite()
    {
        using var f = new Fixture();
        var row = f.Add();
        f.Service.FailDetails = true;
        await f.Vm.SaveCommand.ExecuteAsync(null);
        Assert.IsTrue(row.IsPersistencePending);
        Assert.AreEqual(1, row.Version);
        StringAssert.Contains(f.Vm.ResultSummary, "已保存");
        f.Service.FailDetails = false;
        await f.Vm.RefreshCommand.ExecuteAsync(null);
        Assert.AreEqual(1, f.Service.Creates.Count);
        Assert.IsFalse(f.Vm.HasUnsavedChanges);
    }

    /// <summary>版本冲突不升级版本，拒绝刷新保留编辑；确认只重载当前单。</summary>
    [TestMethod]
    public async Task VersionConflict_RequiresExplicitReload()
    {
        using var f = new Fixture();
        var row = f.Add();
        await f.Vm.SaveCommand.ExecuteAsync(null);
        row.Remark = "我的编辑";
        f.Service.ConflictUpdate = true;
        await f.Vm.SaveCommand.ExecuteAsync(null);
        Assert.IsTrue(row.HasVersionConflict);
        Assert.AreEqual(1, row.Version);
        f.Dialogs.Allow = false;
        await f.Vm.RefreshCommand.ExecuteAsync(null);
        Assert.AreEqual("我的编辑", row.Remark);
        var other = f.Add();
        f.Vm.SelectedItem = row;
        f.Dialogs.Allow = true;
        await f.Vm.RefreshCommand.ExecuteAsync(null);
        Assert.IsFalse(row.HasVersionConflict);
        Assert.IsFalse(row.IsDirty);
        Assert.IsTrue(other.IsDirty);
    }

    /// <summary>本地草稿确认后移除；持久化删除等待服务器成功，响应未知支持原命令恢复。</summary>
    [TestMethod]
    public async Task Delete_LocalAndPersisted_RetryKeepsOriginalReason()
    {
        using var f = new Fixture();
        var local = f.Add();
        f.Dialogs.Allow = false;
        await f.Vm.DeleteSelectedCommand.ExecuteAsync(null);
        Assert.IsTrue(f.Vm.Headers.Items.Contains(local));
        f.Dialogs.Allow = true;
        await f.Vm.DeleteSelectedCommand.ExecuteAsync(null);
        Assert.AreEqual(0, f.Service.Deletes.Count);
        Assert.AreEqual(0, f.Vm.Headers.Items.Count);
        var persisted = f.Add();
        await f.Vm.SaveCommand.ExecuteAsync(null);
        f.DeleteDialogs.Reason = null;
        await f.Vm.DeleteSelectedCommand.ExecuteAsync(null);
        Assert.IsTrue(f.Vm.Headers.Items.Contains(persisted));
        f.DeleteDialogs.Reason = "重复录入";
        f.Service.LoseNextResponse = true;
        await f.Vm.DeleteSelectedCommand.ExecuteAsync(null);
        Assert.IsTrue(f.Vm.Headers.Items.Contains(persisted));
        f.DeleteDialogs.Reason = "后续改变的原因";
        await f.Vm.RefreshCommand.ExecuteAsync(null);
        Assert.AreEqual("重复录入", f.Service.Deletes[1].Reason);
        Assert.AreSame(f.Service.Deletes[0], f.Service.Deletes[1]);
        Assert.AreEqual(0, f.Vm.Headers.Items.Count);
        Assert.IsFalse(f.Vm.HasUnsavedChanges);
    }

    /// <summary>Grid 转换失败或写命令正在执行时，其他入口不能绕过禁止重入门禁。</summary>
    [TestMethod]
    public async Task Save_RejectsInvalidGridEditAndConcurrentWrite()
    {
        using var f = new Fixture();
        f.Add();
        System.ComponentModel.CancelEventHandler reject = (_, e) => e.Cancel = true;
        f.Vm.CommitEditsRequested += reject;
        await f.Vm.SaveCommand.ExecuteAsync(null);
        Assert.AreEqual(0, f.Service.Creates.Count);
        f.Vm.CommitEditsRequested -= reject;
        f.Service.Wait = new();
        var saving = f.Vm.SaveCommand.ExecuteAsync(null);
        Assert.IsTrue(f.Vm.IsBusy);
        Assert.IsFalse(await f.Vm.CanCloseAsync(CancellationToken.None));
        await f.Vm.SaveCommand.ExecuteAsync(null);
        await f.Vm.DeleteSelectedCommand.ExecuteAsync(null);
        Assert.AreEqual(1, f.Service.Creates.Count);
        f.Service.Wait.SetResult();
        await saving;
        Assert.IsFalse(f.Vm.IsBusy);
        Assert.IsFalse(f.Vm.HeaderReadOnly);
    }

    /// <summary>统一构造纯内存会话，来源剩余量小于草稿数量。</summary>
    private sealed class Fixture : IDisposable
    {
        public PersistenceStub Service { get; } = new();
        public DialogStub Dialogs { get; } = new();
        public DeleteDialogStub DeleteDialogs { get; } = new();
        public PostDialogStub PostDialogs { get; } = new();
        public ReverseDialogStub ReverseDialogs { get; } = new();
        public IssueReasonDialogStub IssueReasonDialogs { get; } = new();
        public SwiftExportClient.Authentication.CurrentUserService User { get; } = new();
        public OutboundManagerViewModel Vm { get; }
        public Fixture()
        {
            User.SetCurrentUser(OutboundTestUsers.Create().CurrentUser!);
            Vm = new(new WarehouseStub(), new SourceStub(), Dialogs, persistence: Service, deleteDialogs: DeleteDialogs, postDialogs: PostDialogs, postingUser: User, reverseDialogs: ReverseDialogs, issueReasonDialogs: IssueReasonDialogs);
        }
        public OutboundHeaderModel Add()
        {
            Vm.AddNewHeaderRowCommand.Execute(null);
            var row = Vm.SelectedItem!;
            row.WarehouseSiteId = Guid.NewGuid();
            AddLine(row);
            return row;
        }
        public void AddLine(OutboundHeaderModel row) => row.Lines.AddNew(new OutboundLineDetailDto
        { Uid = Guid.NewGuid(), ProductId = Guid.NewGuid(), Quantity = 10, OnHandQty = 1, UnitSnapshot = "PCS" });
        public void Dispose() => Vm.Dispose();
    }

    /// <summary>模拟服务器命令幂等和详情存储，允许响应丢失但保留提交事实。</summary>
    private sealed partial class PersistenceStub : IOutboundPersistenceService
    {
        private readonly Dictionary<Guid, OutboundDraftCommandResult> _commands = [];
        private readonly Dictionary<Guid, OutboundManagerDetailDto> _details = [];
        public List<CreateOutboundDraftCommand> Creates { get; } = [];
        public List<UpdateOutboundDraftCommand> Updates { get; } = [];
        public List<DeleteOutboundDraftCommand> Deletes { get; } = [];
        public int Writes { get; private set; }
        public int FailCreateAt { get; set; }
        public int FailDeleteAt { get; set; }
        public bool LoseNextResponse, FailDetails, ConflictUpdate, DenyRequests;
        public string LossErrorCode = "OUTCOME_UNKNOWN";
        public TaskCompletionSource? Wait;
        /// <inheritdoc/>
        public async Task<ServiceResult<OutboundDraftCommandResult>> CreateAsync(CreateOutboundDraftCommand command, CancellationToken token)
        {
            Creates.Add(command);
            if (Wait is not null) await Wait.Task;
            if (Creates.Count == FailCreateAt) return ServiceResult<OutboundDraftCommandResult>.Fail(ServiceErrorCodes.Validation, "模拟校验失败");
            return Write(command.CommandId, Guid.NewGuid(), command.Draft, 1, false);
        }
        /// <inheritdoc/>
        public Task<ServiceResult<OutboundDraftCommandResult>> UpdateAsync(Guid uid, UpdateOutboundDraftCommand command, CancellationToken token)
        {
            Updates.Add(command);
            return Task.FromResult(ConflictUpdate ? ServiceResult<OutboundDraftCommandResult>.Fail(ServiceErrorCodes.VersionConflict, "版本冲突")
                : Write(command.CommandId, uid, command.Draft, command.ExpectedVersion + 1, false));
        }
        /// <inheritdoc/>
        public Task<ServiceResult<OutboundDraftCommandResult>> DeleteAsync(Guid uid, DeleteOutboundDraftCommand command, CancellationToken token)
        { Deletes.Add(command); return Task.FromResult(Deletes.Count == FailDeleteAt
            ? ServiceResult<OutboundDraftCommandResult>.Fail(ServiceErrorCodes.Validation, "删除被拒绝")
            : Write(command.CommandId, uid, null, command.ExpectedVersion + 1, true)); }
        /// <inheritdoc/>
        public Task<ServiceResult<OutboundManagerDetailDto>> GetAsync(Guid uid, CancellationToken token)
            => Task.FromResult(FailDetails ? ServiceResult<OutboundManagerDetailDto>.Fail(OutboundErrorCodes.OutcomeUnknown, "详情网络错误") : ServiceResult<OutboundManagerDetailDto>.Ok(_details[uid]));
        private ServiceResult<OutboundDraftCommandResult> Write(Guid command, Guid uid, OutboundDraftData? draft, int version, bool deleted)
        {
            if (DenyRequests) return ServiceResult<OutboundDraftCommandResult>.Fail(ServiceErrorCodes.Forbidden, "权限拒绝");
            if (_commands.TryGetValue(command, out var previous)) return ServiceResult<OutboundDraftCommandResult>.Ok(previous);
            Writes++;
            var result = new OutboundDraftCommandResult { Uid = uid, Version = version, DocumentNo = "IR-test", IsDeleted = deleted };
            _commands.Add(command, result);
            if (draft is not null) _details[uid] = new()
            {
                Header = new() { Uid = uid, Version = version, DocumentNo = result.DocumentNo, WarehouseSiteId = draft.WarehouseSiteId, ShipmentDate = draft.ShipmentDate, Remark = draft.Remark, IssueReason = draft.IssueReason },
                Lines = draft.Lines.Select((x, i) => new OutboundLineDetailDto
                { Uid = x.Uid == Guid.Empty ? Guid.NewGuid() : x.Uid, Version = version, OutboundDocumentId = uid, ProductId = x.ProductId, Quantity = x.Quantity, UnitSnapshot = "PCS", LineNumber = i + 1 }).ToList()
            };
            if (LoseNextResponse) { LoseNextResponse = false; return ServiceResult<OutboundDraftCommandResult>.Fail(LossErrorCode, "模拟提交后响应丢失"); }
            return ServiceResult<OutboundDraftCommandResult>.Ok(result);
        }
    }
    /// <summary>控制是否确认放弃或删除。</summary>
    private sealed class DialogStub : IDialogService
    {
        public bool Allow = true;
        public Task<bool> ConfirmAsync(string title, string message, string primaryButtonText = "确定", string closeButtonText = "取消", CancellationToken cancellationToken = default) => Task.FromResult(Allow);
        public Task ShowContentAsync(string title, object content, string closeButtonText = "关闭", CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ShowErrorAsync(string title, string message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    /// <summary>控制删除原因及取消。</summary>
    private sealed class DeleteDialogStub : IOutboundDeleteDialogService
    {
        public string? Reason = "录入错误";
        public Task<string?> ConfirmAsync(int count, CancellationToken token) => Task.FromResult(Reason);
    }
    /// <summary>不打开真实仓库弹窗。</summary>
    private sealed class WarehouseStub : IWarehousePickerService
    {
        public int Calls;
        public WarehouseSitePickerDto? Result { get; set; }
        public Task<WarehouseSitePickerDto?> PickAsync(CancellationToken cancellationToken = default) { Calls++; return Task.FromResult(Result); }
    }
    /// <summary>不打开真实来源弹窗。</summary>
    private sealed class SourceStub : IOutboundItemDialogService
    {
        public Task<IReadOnlyList<OutboundItemCandidateDto>?> PickAsync(OutboundItemPickerRequest request, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<OutboundItemCandidateDto>?>(null);
    }

    /// <summary>模拟 Refit 返回或传输异常，意外调用其他接口立即失败。</summary>
    private sealed class TransportStub : IOutboundManagerApi
    {
        /// <inheritdoc/>
        public Task<ServiceResult<PagedResponseBase<WarehouseSitePickerDto>>> GetWarehouseOptionsAsync(PagedRequestBase request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        /// <inheritdoc/>
        public Task<ServiceResult<OutboundReversePreviewDto>> PreviewReverseAsync(Guid uid, OutboundReversePreviewRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        /// <inheritdoc/>
        public Task<ServiceResult<OutboundReverseResult>> ReverseAsync(Guid uid, ReverseOutboundCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        /// <inheritdoc/>
        public Task<ServiceResult<PagedResponseBase<OutboundItemCandidateDto>>> SearchItemsAsync(OutboundItemSearchRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        /// <inheritdoc/>
        public Task<ServiceResult<OutboundPostPreviewDto>> PreviewPostAsync(Guid uid, OutboundPostPreviewRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        /// <inheritdoc/>
        public Task<ServiceResult<OutboundPostResult>> PostAsync(Guid uid, PostOutboundCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Exception? Exception;
        public ServiceResult<OutboundDraftCommandResult> Response = null!;
        /// <inheritdoc/>
        public Task<ServiceResult<OutboundDraftCommandResult>> CreateAsync(CreateOutboundDraftCommand command, CancellationToken cancellationToken = default)
            => Exception is null ? Task.FromResult(Response) : Task.FromException<ServiceResult<OutboundDraftCommandResult>>(Exception);
        /// <inheritdoc/>
        public Task<ServiceResult<OutboundDraftCommandResult>> UpdateAsync(Guid uid, UpdateOutboundDraftCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        /// <inheritdoc/>
        public Task<ServiceResult<OutboundDraftCommandResult>> DeleteAsync(Guid uid, DeleteOutboundDraftCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        /// <inheritdoc/>
        public Task<ServiceResult<OutboundManagerDetailDto>> GetAsync(Guid uid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        /// <inheritdoc/>
        public Task<ServiceResult<PagedResponseBase<OutboundManagerListItemDto>>> SearchAsync(OutboundManagerSearchRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
