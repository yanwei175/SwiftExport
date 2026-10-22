---
name: aspdotnet10-webapi-auth-current-user
description: SwiftExport WebApi 认证与当前用户技能。用于 AuthController、JWT 登录、注册、重置密码、ICurrentUser、CurrentUser、DevUserMiddleware、Auth 配置、Bearer Token、角色判断和开发环境免登录相关任务。
---

# WebApi Auth / CurrentUser Skill

## 必读

- `SwiftExport.WebApi/Controllers/Auth/AuthController.cs`
- `SwiftExport.WebApi/Middlewares/DevUserMiddleware.cs`
- `SwiftExport.WebApi/Program.cs`
- `SwiftExport.Core/Interfaces/Auth/**`
- 涉及 Repository 时读 `../dapper-repository/SKILL.md`

## 什么时候使用

- 修改登录、注册、重置密码、`/api/auth/me`。
- 修改 JWT issuer、audience、key、claim。
- 修改 `ICurrentUser` 或当前用户解析。
- 修改开发环境免登录 DevUser 逻辑。
- 修改认证授权中间件顺序。

## 核心规则

- 认证 Controller 是 WebApi 层少数特例，但仍应尽量保持 HTTP 边界清晰。
- JWT claims 至少保持 `NameIdentifier`、`Name`、`sub`、`unique_name` 可被 `ICurrentUser` 解析。
- `DevUserMiddleware` 只允许 Development 且 `Auth:UseDevUser=true` 时生效。
- 生产环境禁止开启 DevUser；`Program.cs` 启动时必须阻断。
- 密码使用 BCrypt，不明文存储。
- 角色判断通过用户角色 Repository 或当前用户上下文完成。

## 详细参考

需要 AuthController、DevUser、安全边界时读：

- `references/认证与当前用户规则.md`
