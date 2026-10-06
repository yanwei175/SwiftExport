using SwiftExport.Application.Interfaces.Auth.Passwords;
using SwiftExport.Application.Services.Auth.Passwords;

namespace SwiftExport.Tools.AdminPasswordRecovery;

/// <summary>校验本机恢复输入并调用受约束存储，不接收角色提升或用户创建请求。</summary>
public sealed class AdminPasswordRecoveryExecutor(
    IAdminPasswordRecoveryStore store,
    IPasswordHasher passwordHasher)
{
    /// <summary>重置一个既有活动 Admin；失败信息不泄露密码或哈希。</summary>
    public async Task<AdminPasswordRecoveryResult> ExecuteAsync(
        string? userName,
        string? newPassword,
        string? confirmation,
        CancellationToken cancellationToken = default)
    {
        var normalizedUserName = userName?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedUserName))
            return new(false, "管理员用户名不能为空。");

        var passwordError = PasswordPolicy.Validate(newPassword, confirmation);
        if (passwordError is not null)
            return new(false, passwordError);

        var status = await store.ResetExistingAdminAsync(
            normalizedUserName,
            passwordHasher.Hash(newPassword!),
            cancellationToken);
        return status switch
        {
            AdminPasswordRecoveryStatus.Updated => new(true, "管理员密码已恢复，既有 Token 已失效。"),
            AdminPasswordRecoveryStatus.TargetNotAdmin => new(false, "目标账号不是活动 Admin，工具拒绝修改。"),
            AdminPasswordRecoveryStatus.TargetNotFound => new(false, "未找到活动目标账号。"),
            _ => new(false, "账号在恢复期间发生变化，请重新运行工具。")
        };
    }
}
