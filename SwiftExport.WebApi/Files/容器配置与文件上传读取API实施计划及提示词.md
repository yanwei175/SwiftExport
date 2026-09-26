# 容器配置与文件上传读取 API 实施计划及提示词

日期：2026-09-26。状态：已实施，待统一 Review。指定执行窗口：01a0dc40-ceff-7873-af01-7406fe888fb7（5.6Sol高）。

## 一、目标和边界

复用已完成的 DvFileStorage，完成服务器 JSON 容器配置、流式上传、读取最小闭环。调用方指定容器标识和相对路径，不能指定服务器绝对目录。返回稳定引用和完整读取地址。

本轮不接产品保存、不改 MainImage、不生成缩略图、不做设置页面、本地/NAS 模式切换、StoredFile、业务临时附件、删除 API、目录列表或上传断点协议。不修改 WPF，不重复实现底层存储。

当前文件库已实现 WriteAsync/OpenReadAsync/GetInfoAsync 等；FileStorageRegistration 按单 RootPath 注册；ProductImageHosting 仍在 /files/ProductImages 提供公开图片。新容器能力由宿主选择多个独立存储实例，保持 DvFileStorage 无 ERP/HTTP 依赖。旧 RootPath 注册和静态入口保留，不自动把旧根目录整体公开。

## 二、权限待确认项（必须遵守）

已向用户提出：新 API 首批已登录活动用户可读取，只有当前真实 Admin 可上传/覆盖；不匿名开放新容器。现有公开图片入口不变。用户答复尚未写入本计划时，这只是建议，不是已批准规则。

执行窗口可先做配置模型、路径/流式操作及替身测试；不得自行确定并开放最终权限、不得部署。主窗口收到用户答复后转发。若答复尚未到达，完成独立工作后报告此项待定，不以超时当同意。若用户选择独立权限，先补齐授权影响范围再执行，不能悄悄扩大到所有登录用户可写。

确认采用建议后：认证沿用 JWT 及账号有效性检查；写权限复用 IPermissionEvaluationService 的活动用户投影 IsAdmin，不只读过时 token role，也不直接在 Controller 写 SQL。权限检查在读取请求体或接触文件之前。权限查询测试用替身，不访问真实数据库。未来业务模块接入时可再设计容器级细权限，本轮不建万能 ACL 平台。

## 三、容器与配置

JSON 增量示例（示例不得自动写入真实配置）：

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

- 顶层 RootPath/Enabled 保留既有语义；Containers 是新 API 的显式白名单，不从顶层自动推导一个全根目录容器。
- 容器标识采用小写字母、数字和短横线，稳定且与物理目录名分离；大小写重复/非法标识、重复或相互包含的容器根目录启动失败，防止隔离被别名绕过。
- 每个容器独立 LocalFileStorage，根目录必须存在，沿用库路径和重解析点校验。启动不创建目录、不探测写权限。缺少 Containers 时新能力无可用容器，旧功能仍可启动。
- ApiBaseUrl 是客户端可达的 API 根地址，可含虚拟路径；有容器时必须是合法 HTTP/HTTPS 绝对地址，无 userinfo/query/fragment。完整读取地址由此生成，不信任 Request.Host。开发可配置实际本机 API 地址。
- MaxUploadBytes 是每容器正整数，首批示例 20 MiB；运行时按此限制流量。不要默认无限制。空容器配置不允许意外落到默认根目录。
- 内部 .dvfilestorage-tmp 不属于可访问文件。不得为容器增加静态目录映射。
- 首批示例使用 Uploads，避免与已有公开 ProductImages 混用。新容器若与已有公开静态目录相同/相互包含，应启动拒绝，防止绕过新读取授权或向公开目录写任意 HTML 等内容。产品主图后续作为明确的业务接入单独设计，不在本轮放宽这条限制。
- 存储实例持有启动配置快照，改配置重启生效；本轮不写 JSON 管理接口、不自动搬迁文件、不重建旧引用。

## 四、HTTP 契约

采用 query path 避免 catch-all 路由解码歧义；配置/返回里的容器标识是逻辑值，不携带磁盘路径。Contracts 放在 DTOs/Files、Apis/Files，保持 netstandard2.0 兼容并遵循现有包装。先核对现有 Refit 版本对 Stream Body 的支持，流不能 JSON 序列化。

| 操作 | 路由及行为 |
| --- | --- |
| 上传 | PUT /api/files/{containerId}?path=docs/a.pdf&overwrite=false；Content-Type: application/octet-stream，原始二进制请求体 |
| 读取 | GET /api/files/{containerId}?path=docs/a.pdf；返回文件流，支持标准 Range |
| 元信息头 | HEAD 同一读取地址，不返回文件体，遵循相同认证与容器检查 |

上传不用 IFormFile、ReadFormAsync 或整文件 MemoryStream。不做 multipart，本轮 Refit 直接传 Stream；调用方负责源流释放，HTTP 控制器不关闭框架拥有的 Request.Body。保持请求取消令牌到存储写入，不在发布成功后误报因晚到取消而失败。

成功返回 ServiceResult<FileUploadResultDto>，DTO 最少含 ContainerId、Path、Reference（如 files/docs/a.pdf）、ReadUrl。Reference 是稳定逻辑引用，ReadUrl 是便捷访问地址，不是文件 ID 或数据库字段。不要为回传长度在发布后重新开文件取“本次结果”，避免并发覆盖后混淆；若需要长度，应使用本次流计数。

overwrite 默认 false，必须显式 true 才覆盖；同名拒绝返回 409，沿用或最小新增明确文件冲突错误码。首批统一成功 200，避免无法可靠判断创建/覆盖而误报 201。

读取新通用容器统一 attachment 下载与 application/octet-stream，设置 nosniff、Cache-Control: private, no-store；不是浏览器图片展示接口，也不允许任意文件作为同源可执行内容。产品公开图片预览仍使用原入口。ReadUrl 不是匿名或签名地址，HTTP 消费者需要携带认证；不能把新受保护地址直接交给不带 Token 的图片缓存就宣称已接通产品预览。

使用框架文件流响应及 enableRangeProcessing；依赖同一次打开的流获得长度与内容，避免单独 Stat 后文件被替换导致 Content-Length 不一致。不实现不可靠的内容 ETag。框架完成/断开请求时释放返回流，HEAD 也不能泄漏流。

错误按现有错误契约返回：未认证 401、禁止 403、未知容器/文件 404、非法路径/参数 400、冲突 409、超限 413、错误上传媒体类型 415、不可预期 I/O 500。取消遵循现有中断处理，不暴露物理路径和异常堆栈。不要把所有 IOException 都映射成“目标已存在”。必要的错误码及 HTTP 映射只做本功能所需增量。

## 五、流式大小与部署边界

- 权限和容器校验先于读取主体。Content-Length 超限立即拒绝；无长度/虚报时仍由计数限制流在 WriteAsync 读取过程中抛出专用超限异常，确保库在发布前清理临时文件。不能等保存成功后再判断超限并删除目标。
- 超限包装流不拥有 Request.Body；对 exact limit、limit+1、空流和 chunked 输入测试。不给源流附加 Seek/Length 能力要求。
- ASP.NET 请求体上限与容器上限一致或允许更大的外层边界，只在该上传端点设置，不全局取消限制。不要在框架已经读取请求体之后再设置限额。
- IIS requestFiltering 有自己的限制，本轮 20 MiB 示例应能在现有外层限制内运行；若以后调大，在使用说明列出 IIS 与应用两层需同时配置，不能单改 JSON 就声称大文件已支持。实际 IIS 行为留给部署验收。
- 不做自动重试。网络结果未知后 overwrite=false 重试可能遇到 409，不据此宣称字节相同或原请求已成功；本轮不是 CommandId 回执系统。

## 六、文件组织与共享边界

- Application/Files：小型文件用例服务与接口，完成写权限协调、调用存储选择器及稳定结果生成；不接收 HttpContext/IFormFile。普通文件操作不套 ERP 单据命令管道。
- Application/Interfaces/Files：容器解析接口及用例所需小型中立模型；应用层只依赖 IFileStorage 抽象，按需添加 Application→DvFileStorage 引用，不在 Core 复制一套存储接口。
- WebApi/Files：配置解析/验证/具体存储实例注册、容器提供器和请求体限制适配；配置 DTO 不塞 Contracts。具体 LocalFileStorage 构造留在宿主组合根。
- WebApi/Controllers/Files：HTTP 路由、状态/文件响应。与现有静态入口并存，不暴露全根目录。
- Contracts：上传结果、必要 Refit 流式方法，参数/返回语义与 Controller 对齐。不为普通流另外造全部 CRUD DTO。
- 不修改 DvFileStorage 的业务边界；确有底层缺陷可做最小修复并补测试，不能把鉴权、容器配置或 HTTP 塞进库。
- 写入仅限上述新文件、必要项目/DI/错误映射及直接相关测试、部署示例/使用说明、本计划。现有 WPF、产品 DTO/服务和真实配置均不改。

## 七、验收与交付

先做实际集成测试宿主（测试认证/权限替身＋独立临时目录），不只测 Controller 方法：

1. 配置缺失/有效/无效，多个容器隔离、重叠拒绝、旧图片入口行为未变。
2. 按最终确认权限验证 401/403 零写入；Admin 上传/覆盖，合法用户读取。
3. 上传二进制后读取字节一致；HEAD 无正文，Range 206/416 与流释放。
4. 默认不覆盖 409、显式覆盖、大小 exact/+1/无长度超限、上传中断及旧内容保留和临时清理。
5. 中文/空格/特殊字符路径与 URL 编码、绝对路径/穿越/编码绕过/保留临时目录拒绝；错误无磁盘路径泄漏。
6. 下载头与不带认证的 ReadUrl 行为，Refit 原始流确实到达服务端而非序列化。

构建后端 Release，运行 DvFileStorage 及 Files/必要错误映射定向测试，不跑 WPF/全量业务、不连接业务库/LocalDB/真实服务器。编译当前源码，不使用旧测试产物。中文职责注释、实际 diff 和空白检查完成后交回 Review。

交付配置示例及手动联调说明：创建独立测试容器目录、管理员配置恰当写权限和内部临时目录权限、合并 JSON、重启后用认证请求上传/读取。只写说明，不实际修改服务器/IIS/ACL/证书。记录真实环境尚未验收，未自动部署。

## 八、给执行窗口的提示词

请实施本文件第 1 至 7 节，只完成容器配置与文件上传/读取 API 最小闭环。先按 AGENTS.md 读取路由和适用规则，使用后端配置、Controller、Contracts/Refit 与错误契约相关 Skill；不套复杂单据事务或 CommandId，不新建 Agent/Skill。

重新核对源码及工作区，保留其他窗口改动。DvFileStorage 已实现并审查完成，应复用。禁止接入产品保存、缩略图、设置页面、删除 API 或 WPF。新 API 只操作显式容器，保留旧 /files/ProductImages 行为。

权限方案须以主窗口转发的用户答复为准；在未确认前先完成不依赖该决定的配置/流式用例与测试，不开放最终权限、不部署。不要自己将建议当批准。

完成后更新本计划的实际文件、权限决策、完整测试命令与结果、部署说明及未验收边界，停在统一 Review。不要提交、重置、连接数据库或改服务器。

## 九、实施记录（2026-09-26）

### 权限决策

用户已明确确认首批权限：新文件 API 不匿名开放；当前活动登录用户可执行 `GET`、`HEAD` 和 Range 读取，仅当前实时权限投影仍为真实 `Admin` 的用户可上传及覆盖。实现通过 `ICurrentUser` 取得当前身份，再调用 `IPermissionEvaluationService.GetProjectionAsync` 验证活动账号和 `IsAdmin`，不直接信任 JWT 中可能过时的角色。账号投影失效返回 `401`，活动普通用户写入返回 `403`。现有公开 `/files/ProductImages` 入口保持不变。

### 实际文件范围

- DvFileStorage：新增 `Exceptions/FileAlreadyExistsException.cs`，让默认拒绝覆盖可与其他 `IOException` 稳定区分；同步更新 `LocalFileStorage.cs`、类库测试和使用说明。
- Contracts：新增 `DTOs/Files/FileUploadResultDto.cs`、`FileErrorCodes.cs` 和 `Apis/Files/IFileApi.cs`。Refit 10 使用 `[Body] Stream` 原始流，不进行 JSON 或完整内存缓冲；下载返回 `HttpResponseMessage` 以保留流和 Range 头。
- Application：新增 `Interfaces/Files` 的容器、访问策略和传输服务边界，以及 `Services/Files` 的实时权限策略、计数限制流和文件用例服务；项目增加对 DvFileStorage 的直接引用并接入 DI。
- WebAPI：新增显式容器配置模型/提供器、`Controllers/Files/FileController.cs` 和容器 API 使用说明；扩展原 `FileStorageRegistration`、错误状态映射和全局 413 出口。未修改既有 `ProductImageHosting` 路由或静态目录范围。
- 测试：新增 `FileAccessPolicyTests.cs`、`FileTransferServiceTests.cs`、`FileApiIntegrationTests.cs`，扩展 `FileStorageRegistrationTests.cs`；使用测试身份、权限替身和随机临时目录，不连接数据库、LocalDB 或服务器。
- 部署示例：仅更新 `_tools/Deployment/服务器配置示例.json`，增加 `ApiBaseUrl` 与 `files` 容器示例；未改任何真实 appsettings、密钥、连接字符串或服务器配置。

### 实际行为

- `FileStorage:Containers` 是显式白名单；无容器时新 API 没有可用存储，旧根目录与图片入口仍可启动。非法/大小写重复标识、非正数上限、非法 API 基址、容器根目录重复/嵌套及与公开 `ProductImages` 重叠均在启动阶段失败。
- 每个容器持有独立 `LocalFileStorage` 快照。`ApiBaseUrl` 支持合法虚拟路径，返回地址逐值编码且不信任 `Request.Host`。
- `PUT /api/files/{containerId}` 只接受 `application/octet-stream`，不读取 multipart 或完整缓冲。已知长度超限在读主体前拒绝；无长度、虚报长度和 chunked 输入由计数流在发布前拒绝并触发 DvFileStorage 临时清理。
- ASP.NET 端点上限只对上传动作设置，并为 chunked 协议保留有限外层开销；文件净字节上限始终由 Application 计数流严格执行。外层 Kestrel 413 统一映射安全错误，不泄漏堆栈或物理路径。
- 默认 `overwrite=false` 使用稳定 `FILE_CONFLICT` 返回 409；其他锁定、权限和磁盘 `IOException` 不会被猜成同名冲突。成功统一返回 200、稳定 Reference 和完整受保护 ReadUrl。
- `GET`/`HEAD` 使用同一次打开的文件流，返回 `attachment`、`application/octet-stream`、`private, no-store`、`nosniff`，并由框架完成 Range 及流释放。不生成 ETag，不增加静态目录映射。

### 验证命令与结果

```powershell
dotnet test .\DvFileStorage.Tests\DvFileStorage.Tests.csproj -c Release
dotnet test .\SwiftExport.Application.Tests\SwiftExport.Application.Tests.csproj -c Release --filter "FullyQualifiedName~SwiftExport.Application.Tests.Files"
dotnet test .\SwiftExport.Application.Tests\SwiftExport.Application.Tests.csproj -c Release --filter "(FullyQualifiedName~HttpErrorContractTests)|(FullyQualifiedName~CommonResultAndPermissionContractTests)"
dotnet build .\SwiftExport-WebAPI.slnx -c Release
```

- DvFileStorage 测试：8/8 通过。
- Files 定向测试：15/15 通过，覆盖容器配置隔离、权限失效、普通用户读不可写、Admin 写、零写入拒绝、Refit 原始流、exact/limit+1、外层 413、冲突/覆盖、特殊字符、路径穿越、GET/HEAD/Range 及流释放。
- 错误契约定向测试：29/29 通过，覆盖既有统一 HTTP 错误包装和稳定错误码约束。
- 后端 Release 构建：成功，0 错误；存在 6 个 `NU1900`，环境无法读取 NuGet 漏洞数据源。测试项目编译仍报告未触碰业务测试中的既有 MSTest 分析器警告。
- 未运行 WPF 或全量业务测试，未连接业务库/LocalDB/服务器/NAS，未执行 ACL、IIS、证书、部署、提交或重置。

### 部署与未验收边界

手动联调步骤见 `容器文件API使用说明.md`：创建独立 `Uploads` 目录、仅授予应用池所需读写权限、合并示例 JSON、重启后使用 Bearer 请求上传与读取。20 MiB 以上调整须同时核对 IIS `maxAllowedContentLength`、代理和 ASP.NET 外层限制。

尚未验证真实 IIS、应用池 ACL、服务器磁盘、HTTPS 证书、反向代理、NAS、进程强杀/断电及生产账号投影查询性能；未实现删除、列表、断点上传、缩略图、签名地址、产品保存、WPF 或设置页面。这些均不在本轮承诺范围。
