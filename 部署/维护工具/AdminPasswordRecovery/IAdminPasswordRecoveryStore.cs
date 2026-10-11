namespace SwiftExport.Tools.AdminPasswordRecovery;

/// <summary>封装本机恢复工具对现有 Admin 账号的条件更新。</summary>
public interface IAdminPasswordRecoveryStore
{
    /// <summary>仅在目标是活动 Admin 时更新密码及凭据版本。</summary>
    Task<AdminPasswordRecoveryStatus> ResetExistingAdminAsync(
        string userName,
        string passwordHash,
        CancellationToken cancellationToken = default);
}
