namespace SwiftExportClient.Features.Inventory.Inbounds.Services;

/// <summary>入库删除原因弹窗协议；取消返回 null。</summary>
public interface IInboundDeleteDialogService
{
    /// <summary>确认本次删除对象数量及共同原因，不执行持久化。</summary>
    Task<string?> ConfirmAsync(int count, CancellationToken token);
}
