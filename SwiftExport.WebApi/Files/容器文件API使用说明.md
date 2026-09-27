# 容器文件 API 使用说明

该 API 只访问宿主 JSON 明确列出的容器，并复用 `DvFileStorage` 完成流式发布和读取。调用方只能提供容器标识和逻辑相对路径，不能指定服务器绝对目录。现有 `/files/ProductImages` 公共图片入口保持独立，新容器不会映射为静态目录。

## 宿主配置

本机开发配置与测试步骤见[产品图片使用说明·本机开发测试](产品图片批量上传与缩略图使用说明.md#本机开发测试)。当前 Development 配置已启用独立的 `files` 容器和产品图片入口；两者使用不同子目录。通用容器配置本身不会启用产品图片上传，产品图片还需要 `ProductImages:UploadEnabled=true`。

以下内容仅用于合并到目标环境配置，不要覆盖真实 JWT、连接字符串或其他服务器设置：

```json
{
  "FileStorage": {
    "Enabled": true,
    "RootPath": "D:\\ERPFiles",
    "ApiBaseUrl": "https://192.168.3.12/",
    "Containers": {
      "files": {
        "RootPath": "D:\\ERPFiles\\Uploads",
        "MaxUploadBytes": 20971520
      }
    }
  }
}
```

- `Enabled` 和顶层 `RootPath` 继续只服务既有产品图片入口和旧单根存储注册。
- `Containers` 是新 API 的白名单，不会从顶层根目录自动生成容器。
- 容器标识只允许小写字母、数字和单个短横线分隔；物理根目录必须已存在。
- `ApiBaseUrl` 是客户端可达的 HTTP/HTTPS API 根地址，可含虚拟路径，但不能含凭据、查询或片段。返回的 `ReadUrl` 不采用请求中的 Host。
- `MaxUploadBytes` 是文件净字节上限。无 `Content-Length` 或虚报长度时，服务仍在流式读取中严格限制。
- 容器根目录不能重复、相互包含，也不能与公开 `ProductImages` 目录或遗留安全保留目录 `.product-image-state` 重复或相互包含；后者不再写 JSON，但仍不能被通用容器接管。

配置是启动快照；修改后需重启。启动只验证目录存在和读取访问，不创建目录、不探测写权限。

## 部署准备

1. 在服务器创建独立容器目录，例如 `D:\ERPFiles\Uploads`。
2. 仅向 WebAPI 应用池身份授予该目录及运行时生成的 `.dvfilestorage-tmp` 所需读写权限，不授予整盘权限。
3. 合并上述 JSON，确认 `ApiBaseUrl` 与客户端实际可达的 HTTPS 地址和虚拟路径一致。
4. 重启 WebAPI 后使用认证请求联调。本任务不自动修改服务器、IIS、ACL 或证书。

20 MiB 示例通常低于 IIS 默认约 30 MiB 请求过滤边界。以后提高 `MaxUploadBytes` 时，必须同时核对 IIS `requestFiltering/requestLimits/maxAllowedContentLength`、反向代理和 ASP.NET 外层限制；只修改 JSON 不代表部署层已经允许更大的请求。

## HTTP 契约

新 API 不匿名开放。服务端在每次操作时读取当前账号的实时活动权限投影：活动登录用户可执行 `GET`、`HEAD` 和 Range 读取；只有当前投影仍为真实 `Admin` 的用户可执行上传和覆盖。账号失效返回 `401`，活动普通用户写入返回 `403`。服务端不会只凭 JWT 中可能过时的角色名称放行。既有公开 `/files/ProductImages` 入口保持原行为，不套用新容器权限。

上传使用原始二进制请求体，不使用 multipart 或 JSON：

```http
PUT /api/files/files?path=docs/manual.pdf&overwrite=false
Authorization: Bearer <token>
Content-Type: application/octet-stream

<binary body>
```

成功统一返回 `200` 和 `ServiceResult<FileUploadResultDto>`：

```json
{
  "success": true,
  "data": {
    "containerId": "files",
    "path": "docs/manual.pdf",
    "reference": "files/docs/manual.pdf",
    "readUrl": "https://192.168.3.12/api/files/files?path=docs%2Fmanual.pdf"
  }
}
```

`overwrite` 默认 `false`；目标存在时返回 `409`，只有显式 `true` 才允许完整替换。网络结果未知后不要因为重试收到 `409` 就推断首次请求一定失败或内容相同。

读取与元信息头使用同一地址：

```http
GET  /api/files/files?path=docs/manual.pdf
HEAD /api/files/files?path=docs/manual.pdf
Range: bytes=0-1023
```

读取统一返回 `application/octet-stream` 和 `attachment`，支持标准 `206`/`416` Range，并设置 `Cache-Control: private, no-store` 与 `X-Content-Type-Options: nosniff`。`ReadUrl` 不是匿名或签名地址，调用方仍需携带认证；不能直接交给不带令牌的图片加载器冒充公开图片预览。

稳定错误状态包括：未认证 `401`、禁止 `403`、未知容器或文件 `404`、非法路径 `400`、目标冲突 `409`、超限 `413`、媒体类型错误 `415`。服务不会在错误响应中返回物理根目录或异常堆栈。

## Refit 调用

共享契约 `IFileApi.UploadAsync` 的 `[Body] Stream` 会由 Refit 10 通过 `StreamContent` 流式发送，默认不缓冲完整文件。调用方负责释放上传源流、下载 `HttpResponseMessage` 及其内容流。下载使用 `HttpResponseMessage` 以保留 Range、响应头和流式正文语义。

## 当前边界

- 通用容器 API 不包含删除、目录列表、断点上传、缩略图、签名地址或静态目录映射。产品图片的专用批量转码入口见[产品图片批量上传与缩略图使用说明](产品图片批量上传与缩略图使用说明.md)，两者不能互相绕过目录和覆盖规则。
- 不接产品保存、`MainImage`、WPF 设置页面或本地/NAS 读取模式。
- 不提供数据库事务、业务附件生命周期或写入幂等回执；`overwrite=false` 重试冲突需要调用方自行确认最终事实。
- IIS、应用池 ACL、真实 HTTPS、代理转发和服务器磁盘行为必须在部署环境另行验收。
