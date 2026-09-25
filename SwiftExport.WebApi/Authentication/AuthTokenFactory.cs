using Microsoft.IdentityModel.Tokens;
using SwiftExport.Application.Models.Auth;
using SwiftExport.Application.Models.Auth.Session;
using SwiftExport.Contracts.DTOs.Auth;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace SwiftExport.WebApi.Authentication;

/// <summary>使用宿主 JWT 配置签发十二小时访问令牌。</summary>
public sealed class AuthTokenFactory(IConfiguration configuration) : IAuthTokenFactory
{
    /// <inheritdoc />
    public LoginTokenDto Create(AuthenticatedUser user)
    {
        var jwtKey = configuration["Auth:JwtKey"]
            ?? throw new InvalidOperationException("Auth:JwtKey 未配置，请检查应用配置。");
        var issuer = configuration["Auth:JwtIssuer"] ?? "SwiftExport";
        var audience = configuration["Auth:JwtAudience"] ?? "SwiftExport";
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.UserId.ToString()),
            new Claim(JwtRegisteredClaimNames.UniqueName, user.UserName),
            new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new Claim(ClaimTypes.Name, user.UserName),
            new Claim(AuthTokenClaims.CredentialVersion, user.CredentialVersion.ToString())
        };
        var token = new JwtSecurityToken(
            issuer,
            audience,
            claims,
            expires: DateTime.UtcNow.AddHours(12),
            signingCredentials: credentials);
        return new LoginTokenDto { Access_token = new JwtSecurityTokenHandler().WriteToken(token) };
    }
}
