namespace SwiftExport.Tools.AdminPasswordRecovery;

/// <summary>描述本机管理员密码恢复结果，不包含密码、哈希或连接信息。</summary>
public sealed class AdminPasswordRecoveryResult(bool success, string message)
{
    /// <summary>恢复是否已完成。</summary>
    public bool Success { get; } = success;

    /// <summary>可安全展示给本机操作者的结果说明。</summary>
    public string Message { get; } = message;
}

/// <summary>描述数据库对恢复目标的安全判定。</summary>
public enum AdminPasswordRecoveryStatus
{
    /// <summary>密码及凭据版本已更新。</summary>
    Updated = 0,
    /// <summary>未找到活动目标账号。</summary>
    TargetNotFound = 1,
    /// <summary>目标没有有效 Admin 角色。</summary>
    TargetNotAdmin = 2,
    /// <summary>目标在更新期间发生变化。</summary>
    ConcurrencyConflict = 3
}
