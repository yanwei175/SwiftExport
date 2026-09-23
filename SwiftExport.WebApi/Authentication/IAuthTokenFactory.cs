using SwiftExport.Application.Models.Auth;
using SwiftExport.Contracts.DTOs.Auth;

namespace SwiftExport.WebApi.Authentication;

/// <summary>在 WebApi 边界根据已认证身份签发 JWT。</summary>
public interface IAuthTokenFactory
{
    /// <summary>创建包含身份及当前凭据版本的访问令牌响应。</summary>
    LoginTokenDto Create(AuthenticatedUser user);
}
