# Caddy 首次安装与日常更新

当前方案：Windows Server 优先，Caddy 提供内网 HTTPS，ASP.NET Core 后端以 Windows 服务运行；PostgreSQL 和文件存储继续使用现有配置。本次未安装 Web 客户端，不需要 Node.js 或 Docker。

当前测试服务器也可以先手工清理旧 IIS，再全新安装，无需走自动切换/恢复流程。在服务器管理员 Windows PowerShell 中运行 `清理旧IIS部署.ps1`：脚本核对站点 `SwiftExport_WebApi`、物理路径 `D:\Apps\SwiftExport_WebApi` 和应用池独立性，备份 IIS 配置后停止并删除该站点及应用池。可以先加 `-WhatIf` 查看范围。脚本不删除程序目录、生产 JSON、`D:\ERPFiles`、证书、PostgreSQL 或 .NET 运行时，也不卸载整个 IIS 角色。

清理完成后，首次安装窗口勾选 **“不迁移 IIS（已清理/新装）”**，其余按本文全新安装流程操作。仍可从保留的旧程序目录选择完整生产配置。清理后若 443 仍被其他程序占用，安装预检会阻止继续；核对实际占用者后再处理，不按进程名批量结束服务。

```text
测试电脑 / WPF / 以后 Web 客户端
              │ HTTPS 443
       SwiftExport_Caddy（内部 CA）
              │ HTTP 127.0.0.1:5081
       SwiftExport_WebApi（Windows 服务）
              ├─ 现有 PostgreSQL
              └─ 现有 D:\ERPFiles 图片及附件目录
```

文件读取、上传和缩略图仍由后端处理，Caddy 代理请求；不会把整个磁盘文件目录直接公开。以后 SPA 前端可以在同一 Caddy 入口增加静态站点路由，这次先完成后端托管。

## 开发电脑准备

1. 在 Visual Studio 右键 `SwiftExport.WebApi` → 发布 → `Windows文件夹`，重新发布当前版本。保留 Release、net10.0、win-x64、框架依赖、不裁剪、不合并单文件。完整发布目录须包含 EXE、DLL、runtimeconfig、deps 和 Windows 服务托管依赖。配置模板及详细步骤见 [可视化发布说明](可视化发布与后端部署工具使用说明.md)。
2. 工具中的 `bin/caddy.exe` 为固定官方 Caddy **2.11.7 Windows x64**。已在开发电脑下载并校验；该目录被 Git 忽略，换电脑或工具包中缺失时运行 `获取Caddy.ps1`。下载校验官方 SHA512，安装时再核对固定 EXE SHA256；不要求服务器联网。升级 Caddy 是独立维护，不自动跟随“最新版本”。
3. 将完整发布目录和部署工具包复制到服务器。工具放在后端、Caddy 和 CA 目录之外，例如 `D:\ERPDeploy\部署工具`，发布包放 `D:\ERPDeploy\发布包\本次版本`。可运行 `打包部署工具.ps1 -OutputPath 'D:\Packages\SwiftExport-Caddy工具.zip'` 生成离线工具包，它按白名单收录工具和 Caddy，不收录真实生产配置、窗口设置或 CA。

不需要在服务器装 SDK。当前发布依赖 x64 ASP.NET Core Runtime 10；已有对应 Hosting Bundle 可继续使用其中的运行时。停止本项目 IIS 不影响运行时安装，不要为这次切换卸载整个 IIS 或 Hosting Bundle。

## 首次安装：从现有测试 IIS 直接切换

双击 `启动Caddy首次安装.cmd`，在 Windows 管理员授权提示中选择“是”；也支持右键“以管理员身份运行”。日常更新入口 `启动后端部署工具.cmd` 同样自动请求管理员权限。请保持启动器、`管理员启动窗口.ps1` 和其他工具文件在同一目录。也可以从后端部署窗口点击“首次安装 Caddy / 切换 IIS”。首次安装窗口只需选择发布目录、完整生产 JSON、服务器 IP 和要停止的 IIS 站点。

默认值与旧部署目录分开：

| 项目 | 默认值 |
| --- | --- |
| 客户端地址 | `https://192.168.3.12/` |
| 停止的旧 IIS 站点 | `SwiftExport_WebApi`，应用池从站点读取 |
| 新后端程序 | `D:\Apps\SwiftExport_Service` |
| 后端运行方式 | `SwiftExport_WebApi` 自动启动服务，虚拟账户 |
| 后端监听 | `http://127.0.0.1:5081` |
| Caddy 程序、配置、切换记录 | `D:\Apps\SwiftExport_Caddy` |
| Caddy 服务 | `SwiftExport_Caddy`，自动启动、独立虚拟账户 |
| 持久 CA、自动续签状态和 Caddy 日志 | `D:\ERPDeploy\CaddyData` |
| 可复制给客户端的证书包 | `D:\ERPDeploy\客户端证书` |

首次安装目标须为空，旧 IIS 程序目录保留。新服务器无需 IIS，勾选“不迁移 IIS”。如果默认目录不合适，可用下面脚本参数指定实际路径。

生产 JSON 必须完整包含现有 **PostgreSQL** `ConnectionStrings.Default`、现有 JWT 密钥及 issuer/audience、文件存储根目录/容器/API 地址和其他服务器设置。沿用服务器的配置，不用历史 SQL Server 配置或开发配置；保留 JWT 密钥。若原配置分散在 `appsettings.json` 和 `appsettings.Production.json`，先整理一份完整生产配置供首次安装，工具不会猜测或合并开发配置。

确认配置没有旧的 `Kestrel:Endpoints` HTTPS/PFX 监听和证书配置，也没有旧 `urls` 或旧 IIS 端口设置。新服务由工具显式传入回环监听地址；工具会拒绝可能覆盖它的显式 Kestrel 端点。对外 `FileStorage:ApiBaseUrl` 应保持客户端使用的 HTTPS 地址。图片和容器目录须已存在，位于程序/CA 目录之外。配置只在服务器受控位置保存。

先点“仅检查配置”，此操作不停止 IIS、不安装服务、不生成 CA。预检会核对：发布依赖、PG/JWT 配置、IP 是否属于此机、端口、目录隔离、同名服务、站点及应用池归属。共享应用池、其他运行站点占用同一 HTTPS 地址或目录冲突时直接停止。

再点“首次安装 / 切换”，工具依次：

1. 解析 Caddy 配置，记录指定旧站点/应用池的运行状态。
2. 停止指定测试站点及其独立应用池，并记录/关闭它们的自动启动，防止重启后抢占端口。保留原站点、绑定、旧证书和程序文件，不并行运行新旧后端。
3. 安装新后端 Windows 服务，仅监听回环地址；授予其程序读取、日志和显式文件存储目录修改权限。
4. 安装 Caddy 服务，由其在独立 CA 目录生成内部 CA 和服务器证书。收紧该目录权限，关闭自动安装根证书及管理 API。
5. 导出根证书公钥，验证 HTTPS 的证书名称、指定 CA 证书链和后端结构化响应；不跳过 TLS 验证，不请求数据库业务接口。
6. 新增 `SwiftExport_Caddy_HTTPS` 防火墙规则：目标 IP 的 TCP 443，Domain/Private 网络、LocalSubnet。HTTP/1.1 和 HTTP/2，不开放 UDP 或后端端口，不改其他规则。服务器应处于域/专用网络；跨子网测试须单独调整网络放行。

工具显示成功后，将证书包复制给测试电脑，并人工验收 WPF 登录、图片 GET/HEAD、上传和本次功能。启动检查不等于数据库或全部业务验收。此次不执行数据库迁移，不修改 PG 结构或数据。

### 脚本入口

在服务器管理员 Windows PowerShell 5.1 中，先检查，移除 `-PlanOnly` 才执行安装：

```powershell
& 'D:\ERPDeploy\部署工具\首次安装Caddy托管.ps1' `
  -PackagePath 'D:\ERPDeploy\发布包\本次版本' `
  -ProductionConfigurationPath 'D:\ERPDeploy\服务器生产配置.json' `
  -ServerAddress '192.168.3.12' `
  -IisSiteName 'SwiftExport_WebApi' `
  -PlanOnly
```

可选参数为 `BackendInstallPath`、`CaddyInstallPath`、`CaddyDataPath`、`CertificateExportPath`、`BackendPort`；新服务器加 `-NoIis`。不要让目录互相包含。

## 测试电脑装一次新证书

只复制 `D:\ERPDeploy\客户端证书`，在测试电脑核对 `证书指纹.txt` 中指纹与服务器安装日志一致，以管理员身份运行 `安装此服务器根证书.cmd`。安装入口只导入校验过指纹的自签 CA 公钥到 `LocalMachine\Root`，拒绝 PFX/私钥。

Windows 信任库供 WPF、Edge、Chrome 使用；使用独立信任库的浏览器需在其配置中信任同一根证书。服务器若也运行测试客户端，可使用同一个证书包安装；Caddy 本身运行不要求服务器全局信任根证书。

不接管旧 IIS 证书，也不需要导出旧 PFX。旧证书和原根信任可保留以支持回退。这次换新 CA 装一次；以后程序更新、Caddy 重启及服务器叶证书自动续签无需重装根证书。[Caddy 内部 HTTPS 说明](https://caddyserver.com/docs/automatic-https)

**长期保留并受控备份 `CaddyData`。其中有根及中间 CA 私钥，只供管理员/服务使用，不能发给客户端、提交源码或放进部署 ZIP。** 普通程序更新不会触碰该目录。换服务器时恢复同一套数据和权限；丢失 CA 或改了 storage 路径会导致客户端重新建立信任。此目录与公开证书导出目录用途不同。

## 以后更新

Visual Studio 发布 → 复制完整发布目录到服务器 → 管理员运行 `启动后端部署工具.cmd` → 选择新发布目录 → 点击“部署 / 更新”。

保持安装目录 `D:\Apps\SwiftExport_Service`、Windows 服务模式、回环地址 `http://127.0.0.1:5081`，首次生产配置留空。首次安装窗口会记住这些设置；如果旧更新窗口一直打开，请关闭后重新打开。工具保留服务器配置和日志、备份旧程序、检查启动并在失败时尝试恢复旧版本。

Caddy 服务、CA、HTTPS 入口、PG 和图片目录都不用重新部署。不要再运行首次安装工具。后端停机更新期间入口可能暂时返回 502，维护前先结束测试客户端的保存/上传操作。更新工具的 HTTP 检查不替代 HTTPS 与功能验收。

Caddy 配置修改是独立维护：先运行 `caddy.exe adapt --config Caddyfile --adapter caddyfile` 检查语法，然后重启 `SwiftExport_Caddy`。管理 API 已关闭，不能使用 `caddy reload`；重启有短暂中断。CA 目录保持原值。以后增加 Web 客户端路由时再安排这一维护。

## 失败和回退

2026-10-08 修复：虚拟服务账户设置使用 `StartPassword = $null`；空字符串不符合 Windows 虚拟账户接口要求。后端与 Caddy 均已修正，失败提示保留 CIM 返回码及账户名。专用替身验证及首次安装/日常更新回归通过，真实服务器账户注册仍需重试验收。[Windows 服务账户参数说明](https://learn.microsoft.com/en-us/windows/win32/api/winsvc/nf-winsvc-changeserviceconfigw)

若遇到旧工具的“服务运行身份设置失败”，先关闭安装窗口并更新部署工具。此错误发生在后端账户设置阶段，Caddy 尚未安装；将默认 `D:\Apps\SwiftExport_Caddy` 程序目录改名保留，例如 `SwiftExport_Caddy_failed_1820`，腾出首次安装目标目录。保留后端 `_failed_` 目录及 `D:\ERPDeploy\CaddyData`、生产 JSON、图片目录，不能把这些一起删除。默认后端目录已由失败恢复流程移出或还原为空；若预检发现其他非空目标或同名服务残留，先核对实际状态。重新打开更新后的首次安装窗口，保持“不迁移 IIS”。

首次安装失败，会撤销本次新建的两项服务和防火墙规则，并尝试恢复旧 IIS 站点/应用池原运行状态及自动启动设置；原本停止的不会被自动启动。失败目录、CA 和日志保留。恢复失败会明确报告，须人工核对。

首次安装工具拒绝接管非空目录。失败后先排查日志和服务是否撤销，在尚未分发根证书时可保留失败目录并改用新的空安装/CA/导出目录再试；**已分发的 CA 不可随意重新生成**，应保留同一 `CaddyData` 进行人工恢复。普通更新失败有旧程序备份，不需要重建 CA。

成功后若需回退 IIS，先停止新后端和 Caddy 并关闭两项新服务的自动启动，再按照 `D:\Apps\SwiftExport_Caddy\IIS切换前状态.json` 恢复旧应用池和站点原运行状态及自动启动设置。旧程序、旧证书和配置都还在；回退前核对旧程序是否兼容当前 PG 与客户端。不要同时启动新旧后端。

## 已验证范围

本机隔离验证：后端解决方案构建、代理头可信边界和文件服务 HTTP 测试；Windows PowerShell 5.1 的首次托管/日常更新替身测试；真实 Caddy 独立回环进程的 TLS、错误 CA/名称拒绝、根证书仅公钥导出、重启保持 CA 不变；两个窗口布局构建。验证不注册系统服务、不操作实际 IIS、防火墙、信任库或数据库。

2026-10-08 本轮记录：后端 build 成功；74 项代理、文件与首批 Web HTTP 定向测试通过、无跳过；首次安装/更新隔离验证通过，包含 IIS 原自动启动设置恢复；两个窗口分别 16/17 个控件构建通过；Visual Studio `Windows文件夹` 配置实际发布成功，已检查 Windows 服务依赖和原生图片库，并排除 appsettings。最初发布缺少 win-x64 资产，补还原后通过，未修改发布配置。构建存在既有 NU1900 与 MSTest 分析器警告。

真实服务器的 SCM 注册、虚拟账户 ACL、开机启动、实际网络访问和业务功能仍需执行安装后验收。此工具已准备和本机验证，不代表服务器已经完成切换。参考 [Caddy Windows 服务](https://caddyserver.com/docs/running#windows-service)、[Caddy 配置选项](https://caddyserver.com/docs/caddyfile/options)、[ASP.NET Core 代理接线](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0)。
