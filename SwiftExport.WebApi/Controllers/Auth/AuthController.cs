using BCrypt.Net;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Data;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;

namespace SwiftExport.WebApi.Controllers.Auth;

/// <summary>
/// 认证接口：登录获取 JWT、查询当前用户（含角色）、管理员注册新用户。
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController(
    ISysUserRepository userRepo,
    ISysUserRoleRepository userRoleRepo,
    ICurrentUser currentUser,
    DbSession dbSession,
    IConfiguration config) : ControllerBase
{
    private readonly ISysUserRepository _userRepo = userRepo;
    private readonly ISysUserRoleRepository _userRoleRepo = userRoleRepo;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly DbSession _dbSession = dbSession;
    private readonly IConfiguration _config = config;

    // ──────────────────────────────────────────────────────
    // POST /api/auth/login
    // ──────────────────────────────────────────────────────

    /// <summary>用户名+密码登录，返回 JWT access_token</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { message = "用户名和密码不能为空" });

        var user = await _userRepo.GetByUserNameAsync(request.UserName);
        if (user == null)
            return Unauthorized(new { message = "用户名或密码错误" });

        // 验证 BCrypt 哈希密码
        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return Unauthorized(new { message = "用户名或密码错误" });

        var token = GenerateJwt(user.Id, user.UserName);
        return Ok(new { access_token = token });
    }

    // ──────────────────────────────────────────────────────
    // GET /api/auth/me
    // ──────────────────────────────────────────────────────

    /// <summary>获取当前登录用户信息（含角色列表，需携带 JWT）</summary>
    [HttpGet("me")]
    [Authorize]  // 强制解析 Bearer Token
    public async Task<IActionResult> Me()
    {
        if (User?.Identity?.IsAuthenticated != true)
            return Unauthorized(new { message = "未登录" });

        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier)
                     ?? User.FindFirstValue("sub");
        var userName  = User.FindFirstValue(ClaimTypes.Name)
                     ?? User.FindFirstValue("unique_name");

        // 查询用户角色列表
        var roles = new List<string>();
        if (int.TryParse(userIdStr, out var userId))
        {
            var userRoles = await _userRoleRepo.GetByUserIdAsync(userId);
            roles = userRoles
                .Where(r => !string.IsNullOrEmpty(r.RoleName))
                .Select(r => r.RoleName!)
                .ToList();
        }

        // 查询昵称
        var user = await _userRepo.GetByIdAsync(userId);

        return Ok(new
        {
            UserId   = userIdStr,
            UserName = userName,
            NickName = user?.NickName ?? userName,
            Roles    = roles
        });
    }

    // ──────────────────────────────────────────────────────
    // POST /api/auth/register  （仅管理员）
    // ──────────────────────────────────────────────────────

    /// <summary>
    /// 注册新用户。
    /// 规则：当前登录用户必须拥有 Admin 角色，否则返回 403。
    /// </summary>
    [HttpPost("register")]
    [Authorize]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        // ① 必须已登录
        if (!_currentUser.IsAuthenticated)
            return Unauthorized(new { message = "请先登录" });

        // ② 查角色：必须拥有 Admin 角色才能注册新用户
        if (_currentUser.UserId == null)
            return StatusCode(403, new { message = "无法识别当前用户身份" });

        var hasAdmin = await _userRoleRepo.UserHasRoleAsync(_currentUser.UserId.Value, "Admin");
        if (!hasAdmin)
            return StatusCode(403, new { message = "仅拥有 Admin 角色的账号可注册新用户" });

        // ③ 基础校验
        if (string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { message = "用户名和密码不能为空" });

        if (request.Password != request.ConfirmPassword)
            return BadRequest(new { message = "两次密码不一致" });

        if (request.Password.Length < 6)
            return BadRequest(new { message = "密码长度不能少于6位" });

        // ④ 用户名重复检查
        var existing = await _userRepo.GetByUserNameAsync(request.UserName);
        if (existing != null)
            return BadRequest(new { message = $"用户名 {request.UserName} 已存在" });

        // ⑤ 哈希密码并插入
        var hash = BCrypt.Net.BCrypt.HashPassword(request.Password);
        const string sql = """
            INSERT INTO [SysUser] ([UserName],[NickName],[PasswordHash],[Email],[PhoneNumber],[IsActive],[IsDeleted],[CreatedDate],[CreatedBy])
            OUTPUT INSERTED.Id
            VALUES (@UserName,@NickName,@PasswordHash,@Email,@PhoneNumber,1,0,@CreatedDate,@CreatedBy)
            """;

        var newId = await _dbSession.Connection.ExecuteScalarAsync<int>(sql, new
        {
            request.UserName,
            NickName = string.IsNullOrWhiteSpace(request.NickName) ? request.UserName : request.NickName,
            PasswordHash = hash,
            request.Email,
            request.PhoneNumber,
            CreatedDate = DateTime.Now,
            CreatedBy = _currentUser.UserName
        });

        return Ok(new { Success = true, Data = newId, Message = $"用户 {request.UserName} 注册成功" });
    }

    // ──────────────────────────────────────────────────────
    // 私有：生成 JWT
    // ──────────────────────────────────────────────────────

    private string GenerateJwt(int userId, string userName)
    {
        var jwtKey = _config["Auth:JwtKey"]
            ?? throw new InvalidOperationException("Auth:JwtKey 未配置，请检查 appsettings.json");
        var issuer = _config["Auth:JwtIssuer"] ?? "SwiftExport";
        var audience = _config["Auth:JwtAudience"] ?? "SwiftExport";

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.UniqueName, userName),
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Name, userName),
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(12),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

/// <summary>登录请求体</summary>
public record LoginRequest(string UserName, string Password);

/// <summary>注册请求体</summary>
public record RegisterRequest(
    string UserName,
    string Password,
    string ConfirmPassword,
    string NickName,
    string Email,
    string PhoneNumber);
