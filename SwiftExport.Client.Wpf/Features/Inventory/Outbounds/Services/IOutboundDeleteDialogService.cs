namespace SwiftExportClient.Features.Inventory.Outbounds.Services;

/// <summary>出库删除原因弹窗协议；取消返回 null。</summary>
public interface IOutboundDeleteDialogService
{
    /// <summary>确认本次删除对象数量及共同原因，不执行持久化。</summary>
    Task<string?> ConfirmAsync(int count, CancellationToken token);
}
