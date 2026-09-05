namespace SwiftExportClient.Authentication;

/// <summary>描述客户端结束当前认证会话的原因。</summary>
public enum SessionEndReason
{
    /// <summary>用户主动退出。</summary>
    Logout,
    /// <summary>服务端拒绝当前认证凭据。</summary>
    Unauthorized,
    /// <summary>本人密码已修改，旧凭据必须立即退出。</summary>
    CredentialsChanged
}
