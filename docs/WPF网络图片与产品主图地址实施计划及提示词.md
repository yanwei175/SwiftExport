# WPF 网络图片加载实施计划及提示词

日期：2026-09-26。本版按用户最新要求替换上一版跨层计划；只实施 WPF 网络图片加载。旧版 A/B、MainImageUrl 和 FileStorage.PublicBaseUrl 方案不再属于本轮任务。

## 目标

让现有 CachedImage / IImageCacheService 在收到完整 HTTP/HTTPS 图片地址时能异步显示图片；失败显示空图。原本地绝对路径加载行为保持不变。

例如传入 https://192.168.3.12/files/ProductImages/test.jpg 即可显示。服务器目前只有可用的静态图片读取入口，完整文件服务尚未建立；本轮无需等待文件服务，也不建设文件服务。

## 实施范围

主要文件：

- SwiftExport.Client.Wpf/Services/Images/ImageCacheService.cs
- SwiftExport.Client.Wpf/Services/Images/ImageCacheOptions.cs（仅确有需要时）
- SwiftExport.Client.Wpf/Bootstrapper/ServiceCollectionExtensions.cs（网络客户端注册）
- SwiftExport.Client.Wpf/Resources/Controls/CachedImage.cs（仅必要注释或接线，保留现有生命周期逻辑）
- SwiftExport.Client.Wpf.Tests/Images 中直接相关测试

不修改 Application、WebApi、Core、Infrastructure、Contracts、产品页面业务和数据库。不增加 MainImageUrl 或地址转换服务，不更改 MainImage 的存储方式，不新增 PublicBaseUrl 配置，不批量改图片引用，不增加上传、删除、缩略图或附件功能。

## 实现要求

1. 在现有缓存入口识别完整 HTTP/HTTPS 地址并增加网络分支；缓存不拼接地址、不判断产品业务。相对路径不在本轮处理。
2. 使用可复用 HttpClient 异步读取图片；不携带业务 API 的 Bearer Token、Cookie 或默认凭据，不绕过 HTTPS 证书校验。
3. 网络图片只使用现有有容量上限的内存缓存，不写磁盘文件；保留原本地图片及磁盘缩略图缓存，不清理用户文件。
4. 复用现有源文件大小、解码宽度和并发限制。读取响应体时也限制实际字节数，避免只检查 Content-Length；网络读取设置合理超时（默认 15 秒）。不为此建立新框架。
5. 后台解码、冻结后交给控件。失败、超时、无效图片返回空图，不弹业务提示；复用现有短负缓存。正常取消与失败区分。
6. 网络缓存键包含完整 URL 和解码宽度，路径大小写不得混淆。同 URL 内容可能被替换，采用简单 60 秒内存有效期：到期后下一次加载重新获取，不要求 ETag、自动刷新、刷新按钮或实时推送。
7. 复用现有并发共享和控件取消/版本检查，避免一行晚到的图片显示到另一行；单个消费者取消不能破坏其他消费者。只修本轮直接涉及的资源释放问题，不重构整套缓存。
8. 更新涉及类型和方法的中文职责注释。保持原有接口及本地消费者兼容，不改 ViewModel 业务流程。

## 配置和使用

日常开发 WPF 的 Api.BaseUrl 可以切回 https://localhost:7286，先确认本机 WebAPI 正在监听该地址；服务器验收时才改为 https://192.168.3.12。这与加载完整图片 URL 相互独立：本机 API 模式下也能读取服务器 HTTPS 图片。

本轮不自动改用户 JSON。Product.BaseImageFolder 和原缓存配置保留，不需要在服务器建立缩略图文件夹。

实施完成后，图片控件收到完整 URL 就能显示；原有本地引用不会自动变成网络地址。优先用测试控件或测试夹具传入现有 test.jpg 地址验证，不自动改产品数据库。产品固定相对引用与地址生成留待后续文件能力设计。

## 最小验证

使用可控 HTTP 替身和现有图片测试验证：

- 成功解码、内存重复读取及 TTL 后重新获取。
- 404、非图片、超时、超限（含无 Content-Length）返回空图。
- 网络分支不产生磁盘缓存文件，本地图片现有测试仍通过。
- 相关取消/并发共享，以及控件切换来源时旧结果不会覆盖新图。

构建 WPF 和直接相关测试项目，运行 Images 定向测试即可，不扩到全量业务测试。测试基于当前源码编译产物，不连接业务库或 LocalDB。真实服务器 HTTPS 可由用户联调，自动测试不依赖服务器在线。

本计划已按收窄后的 WPF 范围实施，实际结果见文末“实施记录”。

## 直接转发提示词

```text
请按 docs/WPF网络图片与产品主图地址实施计划及提示词.md 的最新内容实施。注意文档已收窄：本轮只让 WPF 现有图片缓存支持完整 HTTP/HTTPS 地址，不再执行旧版跨层 A/B 计划。

先读取 AGENTS.md、前端工作流及对应图片缓存/运行时安全 Skill，核对 Services/Images、CachedImage、注册和现有 Images 测试。

在现有缓存中增量增加网络分支：异步获取、后台解码、失败空图；HTTP 图片只用受限内存缓存，不写磁盘；保留本地路径及原磁盘缓存。复用现有大小、解码宽度、并发、取消和防旧结果覆盖机制，设置网络超时和简单的 60 秒内存有效期。HttpClient 复用、不附加业务令牌、不绕过证书校验，不搭建新的缓存或文件框架。

禁止修改后端、Contracts、Product.MainImage、产品页面业务或数据库，不新增 MainImageUrl、PublicBaseUrl、地址拼接服务、上传、缩略图和刷新按钮；不修改用户真实 JSON。不需要完整文件服务，现有 HTTPS test.jpg 已可供用户联调。

只构建 WPF 及相关测试项目并运行 Images 定向测试，不跑全量业务测试，不连接业务库/LocalDB，不自动部署、提交、重置或清理无关文件。完成后记录实际修改与验证结果，停在 Review 边界。如其他窗口已经开始旧计划，先由用户确认它停止后再接管，不能覆盖并行改动。
```

## 实施记录（2026-09-26）

### 实际修改范围

- `SwiftExport.Client.Wpf/Services/Images/ImageCacheService.cs`
- `SwiftExport.Client.Wpf/Services/Images/ImageCacheOptions.cs`
- `SwiftExport.Client.Wpf/Services/Images/IImageCacheService.cs`
- `SwiftExport.Client.Wpf/Bootstrapper/ServiceCollectionExtensions.cs`
- `SwiftExport.Client.Wpf/Resources/Controls/CachedImage.cs`
- `SwiftExport.Client.Wpf.Tests/Images/ImageCacheServiceTests.cs`

未修改后端、Contracts、产品页面业务、`Product.MainImage`、数据库和用户 JSON；未新增 `MainImageUrl`、`PublicBaseUrl`、地址拼接、上传、缩略图或刷新功能。

### 实际行为

- 现有入口现在识别完整 HTTP/HTTPS 地址；缓存键包含调用方传入的完整 URL 和解码宽度，路径及查询串大小写不混淆。
- 网络响应使用独立、复用的命名 `HttpClient`，不进入业务 Bearer 管道，禁用 Cookie 和默认凭据，保留平台默认 HTTPS 证书校验。
- 网络请求默认 15 秒超时，响应按流读取并执行实际字节上限；下载后在后台解码并冻结位图。
- 网络图片复用现有内存容量、解码宽度、并发共享、消费者取消和控件版本检查，采用 60 秒内存有效期及现有失败短负缓存；单个消费者取消不终止同键共享请求。
- 网络图片不读写磁盘缓存；本地绝对路径、UNC 路径和原磁盘缩略图缓存行为保留。HTTP 状态失败、损坏图片、超时或超限均返回空图。

### 验证命令与结果

```powershell
dotnet build .\SwiftExport.Client.Wpf\SwiftExport.Client.Wpf.csproj --no-restore
dotnet build .\SwiftExport.Client.Wpf.Tests\SwiftExport.Client.Wpf.Tests.csproj --no-restore
dotnet test .\SwiftExport.Client.Wpf.Tests\SwiftExport.Client.Wpf.Tests.csproj --no-restore --filter "FullyQualifiedName~SwiftExport.Client.Wpf.Tests.Images"
```

- WPF 项目构建：成功，0 错误；存在 `NU1900`，隔离环境无法读取 NuGet 漏洞数据源。
- WPF 测试项目构建：成功，0 错误；除同一 `NU1900` 外，存在未触碰的 Navigation 可空性和其他测试分析器警告。
- Images 定向测试：12/12 通过，0 失败，0 跳过。
- 自动验证未连接业务库或 LocalDB，未访问真实服务器，未部署、提交或重置。服务器 `test.jpg` 留给 Review 阶段人工联调。

### Review 结论

- 已复核网络与本地缓存键、60 秒 TTL、失败短负缓存、同键共享、单消费者取消、控件旧结果门闩、响应体限量、后台冻结、磁盘隔离及独立 HTTP 管道。
- Review 中恢复了本地失败缓存原有的路径大小写语义，避免网络分支改变本地行为；网络 URL 仍按完整原文哈希，不会混淆路径或查询串大小写。
- Review 中把已知响应长度的初始内存预分配限制为 1 MiB，实际内容仍按既有源文件上限流式读取，避免可信响应头造成不必要的大块预分配。
- 测试同时覆盖有/无 `Content-Length` 的超限响应，并确认失败短负缓存不会重复发送同键请求。最终无本轮范围内的阻断性 Review 发现。
- 共享工作树另有其他窗口留下的后端、解决方案、部署、WPF 项目文件和真实 `appsettings.json` 改动；本轮未修改、回退或验收这些范围外内容。
