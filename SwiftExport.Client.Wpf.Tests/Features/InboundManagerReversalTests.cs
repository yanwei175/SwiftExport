using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Reversal;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Inventory.Inbounds.Services;

namespace SwiftExport.Client.Wpf.Tests.Features;

/// <summary>反过账窗口恢复协议：未知结果重试原命令，成功后详情失败只重读。</summary>
public sealed partial class InboundManagerPersistenceTests
{
    /// <summary>响应丢失不允许重新编辑，刷新只重放原命令。</summary>
    [TestMethod]
    public async Task Reverse_UnknownKeepsCommandAndOtherEdits()
    {
        using var f = new Fixture(); var row = f.Add();
        await f.Vm.SaveCommand.ExecuteAsync(null); await f.Vm.PostCommand.ExecuteAsync(null);
        f.Service.LoseReverseResponse = true;
        await f.Vm.ReverseCommand.ExecuteAsync(null);
        Assert.IsTrue(row.IsPersistencePending);
        Assert.IsFalse(await f.Vm.CanCloseAsync(CancellationToken.None));
        await f.Vm.RefreshCommand.ExecuteAsync(null);
        Assert.AreEqual(InboundPostingStatusEnum.Reversed, row.PostingStatus);
        Assert.AreEqual(1, f.Service.ReverseWrites);
        Assert.AreSame(f.Service.Reverses[0], f.Service.Reverses[1]);
    }

    /// <summary>写已成功即标记只读；详情失败不重复扣减。</summary>
    [TestMethod]
    public async Task Reverse_AcceptedDetailFailureOnlyReadsAgain()
    {
        using var f = new Fixture(); var row = f.Add();
        await f.Vm.SaveCommand.ExecuteAsync(null); await f.Vm.PostCommand.ExecuteAsync(null);
        f.Service.FailDetails = true;
        await f.Vm.ReverseCommand.ExecuteAsync(null);
        Assert.AreEqual(InboundPostingStatusEnum.Reversed, row.PostingStatus);
        Assert.IsTrue(row.IsPersistencePending);
        f.Service.FailDetails = false;
        await f.Vm.RefreshCommand.ExecuteAsync(null);
        Assert.AreEqual(1, f.Service.Reverses.Count);
        Assert.IsFalse(row.IsPersistencePending);
    }

    private sealed class ReverseDialogStub : IInboundReverseDialogService
    {
        /// <inheritdoc/>
        public Task<string?> ConfirmAsync(InboundReversePreviewDto preview, CancellationToken token) => Task.FromResult<string?>("录入错误");
    }
    private sealed partial class PersistenceStub
    {
        public int ReverseWrites;
        public bool LoseReverseResponse;
        public List<ReverseInboundCommand> Reverses { get; } = [];
        private readonly Dictionary<Guid, InboundReverseResult> _reverseCommands = [];
        /// <inheritdoc/>
        public Task<ServiceResult<InboundReversePreviewDto>> PreviewReverseAsync(Guid uid, InboundReversePreviewRequest request, CancellationToken token) =>
            Task.FromResult(ServiceResult<InboundReversePreviewDto>.Ok(new() { Uid = uid, Version = request.ExpectedVersion, CanReverse = true }));
        /// <inheritdoc/>
        public Task<ServiceResult<InboundReverseResult>> ReverseAsync(Guid uid, ReverseInboundCommand command, CancellationToken token)
        {
            Reverses.Add(command);
            if (!_reverseCommands.TryGetValue(command.CommandId, out var result))
            {
                ReverseWrites++;
                var detail = _details[uid]; detail.Header.Version++; detail.Header.PostingStatus = InboundPostingStatusEnum.Reversed;
                result = new() { Uid = uid, Version = detail.Header.Version, DocumentNo = detail.Header.DocumentNo,
                    PostingStatus = InboundPostingStatusEnum.Reversed, ReversedDate = DateTime.Now, ReverseReason = command.Reason };
                _reverseCommands.Add(command.CommandId, result);
            }
            if (LoseReverseResponse) { LoseReverseResponse = false; return Task.FromResult(ServiceResult<InboundReverseResult>.Fail(ServiceErrorCodes.OutcomeUnknown, "响应丢失")); }
            return Task.FromResult(ServiceResult<InboundReverseResult>.Ok(result));
        }
    }
}
