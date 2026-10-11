namespace SwiftExportClient.Updates;

/// <summary>更新进程前复用业务页面的编辑提交及离开确认协议。</summary>
public interface IClientRestartGuard
{
    /// <summary>页面初始化或操作中、用户拒绝离开时返回 false，不销毁页签。</summary>
    Task<bool> CanRestartAsync(CancellationToken cancellationToken);
}
