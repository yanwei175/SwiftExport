# Web 首批接口契约验收

验收日期：2026-10-07。目标是为后续产品 Web 页面建立可生成、可调用、可检查的 HTTP 契约；本轮没有开发业务前端。

> **已证明（2026-10-07 指定范围）**：首批 OpenAPI 描述、Orval 客户端生成、指定隔离 HTTP 契约与模拟 Fetch 场景可用。
>
> **尚未证明**：当前整合版本全套通过、真实 PostgreSQL 写入/并发、真实浏览器及托管联调通过。隔离快照不能替代这些证据。
>
> **2026-10-08 文档修订**：保留以下历史命令/计数/失败，补充后续实施门槛。当前源码的权限数量断言已为 235（本次只读核对），不是仍停留在 232；本轮未重新运行测试，不能把这项静态变化写成当前全套通过。

<a id="browser-session-facts"></a>

## 2026-10-09 浏览器会话定向补验

按用户授权新增以下会话 HTTP 能力，未修改 Product DTO、事务、权限或 BatchSave 行为。协议规则正文只维护在 [公共设施基线](Web客户端架构与脚手架决策.md#web-common-services)，前端整合证据见 [骨架记录](../SwiftExport.WebApp/docs/骨架验收记录.md#公共设施计划实施与最终整合验收)。

| 新操作 | 指定范围事实 |
| --- | --- |
| `GET /api/auth/browser-session/csrf` | 裸 BrowserSessionCsrfDto，复用 ASP.NET 防伪令牌；浏览器登录/退出核验 |
| `GET /api/auth/browser-session/restore` | 仅 Cookie 走既有 JWT/实时凭据版本验证，返回原 LoginTokenDto；严格原 exp，不续签或重新设置 Cookie |
| `POST /api/auth/browser-session/logout` | CSRF 验证后删除恢复 Cookie，204；旧 JWT 的服务器撤销未增加 |

已有 login 保留 WPF 的 DTO 与无 Cookie 行为，只有明确 Web 标记才增加防伪与原 JWT Cookie。真实 HTTPS 隔离宿主验证混用 Bearer、缺失/损坏防伪、Cookie 属性/期限、不续期、过期及凭据版本变化被拒绝；普通业务仍只接 Bearer，Cookie 不授权普通端点。

`FirstWebHttpContractTests` 与 `BrowserSessionHttpTests` 最终共 **38/38 通过、0 跳过**；使用真实 Controller/JWT/Antiforgery 管道，业务及凭据数据端口为隔离夹具，没有连接 PostgreSQL。当前真实 Swagger 导出加审核切片/Orval/权限投影已通过，Web 客户端含 12 个审核操作，PermissionCatalog.All 投影为 116 个键。运行客户端仅 WebApp 一份；历史工具生成样例不成为第二套客户端。

这些证据不证明真实账号登录、运行库权限/候选组合或生产 Cookie 托管已通过；也不关闭下文 Product 稳定结果分类、回滚确认及恢复查询缺口。2026-10-07 历史日期、命令、计数及未通过项继续保留如下。

## 2026-10-07 范围与实现

主角色为 Backend，采用后端 API 工作流。写入限于 WebApi 契约描述/配置、相关测试、独立生成验证工具和本报告。本轮没有修改 Application/Core/Infrastructure 业务行为，没有修改共享 DTO、权限策略、CORS 或令牌策略，没有运行 PostgreSQL 写入、部署、提交或推送。

实际读取产品 WPF 调用链后，首批依赖从基础四个端点补齐到九个；复用已有读取端点，没有为 Web 新增 API。MaterialType/SupplyType 来自静态字符串选项，Unit 使用现有按 Code 查询。产品图片字段保留在 DTO 中；文件/上传接口不属于此轮实际依赖。

| HTTP 端点 | 请求 | 成功正文 | 授权要求 |
| --- | --- | --- | --- |
| `POST /api/auth/login` | `LoginRequestDto`，必需非空 userName/password | 裸 `LoginTokenDto`，`access_token` | 匿名登录 |
| `GET /api/auth/me` | 无 | 裸 `MeDto` | 有效 JWT、实时凭据版本 |
| `POST /api/product/page` | `ProductSearchRequest` | `ServiceResult<PagedResponseBase<ProductDto>>` | `Products.Product.View` |
| `POST /api/product/batch-save` | `ProductBatchSaveRequest` | `ServiceResult<ProductBatchSaveResponse>` | 按集合检查 `Products.Product.Create/Edit/Delete` |
| `GET /api/productcategory/all` | 无 | `ServiceResult<IReadOnlyList<ProductCategoryDto>>` | `Products.Category.View` |
| `GET /api/sysdictdata/by-code/{dictCode}` | 路径 Code；产品单位使用 `Unit` | `ServiceResult<IReadOnlyList<SysDictDataDto>>` | `System.Dictionary.View` |
| `GET /api/productcolor/all` | 无 | `ServiceResult<IReadOnlyList<ProductColorDto>>` | `Products.Color.View` |
| `POST /api/supplier/page` | `SupplierSearchRequest` | `ServiceResult<PagedResponseBase<SupplierDto>>` | `Suppliers.Supplier.View` |
| `GET /api/product/default-warehouse-candidates` | Keyword/PageIndex/PageSize 查询参数 | `ServiceResult<PagedResponseBase<ProductDefaultWarehouseCandidateDto>>` | `Products.Product.View` |

Swagger 路径沿用现有小写路由配置。POST 发布本轮验收的 `application/json`；查询参数保留生成文档中的名称。现有服务器仍使用原来的绑定和序列化设置。

## 已有约定的验证

以下主体保留 2026-10-07 的首批范围。2026-10-09 新增浏览器会话的指定事实见 [定向会话补验](#browser-session-facts)，不能用它替代原报告未通过的组合测试或 Product 真实写入门槛。

- 登录/me 的成功正文没有 `success/data` 包装；失败仍由统一 `ServiceResult<object>` 出口返回。
- 产品分页是 POST page，包含分页与筛选；没有替换为简化 search 接口。
- 路由身份、BaseService 外层 Version 和此前五处权限修复使用既有回归测试验证，未重复修改实现。
- 混合产品批次逐类验证 Create/Edit/Delete，缺一项权限即拒绝；没有进入写服务。
- 分类、字典、颜色、供应商有独立读取权限。仓库使用现有产品候选接口，未扩展库存管理权限。
- JSON 字段为 camelCase，保留显式 null；SaveOperation 为 `Create/Update/Delete` 字符串，Guid 为 uuid 字符串，日期为 date-time 字符串，decimal 为 number。
- HTTP 400/401/403/422/429/500/503 的统一错误正文保留 code/message/field；相关响应保留调用追踪头，401 Bearer 挑战和 429 Retry-After 由现有管道产生。
- 401 验证使用真实签发及校验 JWT，包含无效令牌和凭据版本失效。登录空字段走真实服务的输入校验；损坏 JSON、空正文和错误 Guid 走真实 MVC 绑定。

## 本轮补齐与缺陷证据

有效基线的 HTTP 测试为 11 通过、1 失败：Swagger 的封闭泛型 schema ID 带程序集限定内容，不符合组件名称规则。基线实际导出另显示整数枚举，而 HTTP 使用字符串枚举；登录/me 的 200 缺少正文 schema。这些是生成描述与运行事实偏差。

本轮补齐：

1. 保留完整命名空间隔离同名 DTO，递归生成合法泛型 schema ID。
2. 使用与 MVC 一致的 `Swashbuckle.AspNetCore.Newtonsoft 10.2.3` 描述枚举和字段；引用属性允许表达 data/error 的 null。
3. 为九个端点补稳定 operationId、实际成功类型、既有错误出口、JSON、Bearer 和必要认证字段描述。没有把泛型行级冲突统一提升为 HTTP 409。
4. 从临时 HTTP 宿主实际导出 OpenAPI，选择九个端点及其 30 个引用组件，固定 Orval `8.40.0` 生成 Fetch 客户端，TypeScript `7.0.2` 严格检查。
5. Fetch 验收曾发现生成器默认没有编码字典路径参数；启用 `urlEncodeParameters` 后，对包含空格/斜杠/中文的路径值和仓库查询值验收通过。

新增组件身份保持命名空间，生成类型名因此较长；没有手工简化 DTO 或修补生成代码。HTTP 错误响应通过带 status 的联合类型消费；网络错误和取消走异常。生成样例可消费真实 code、field、message 和 correlationId。

## 产品批量保存的实际边界

真实 `ProductMaintenanceService` 使用最外层 Serializable 事务，对整批校验及写入。失败不回传可接纳的部分成功行；成功才返回完整整批回执。失败出口能否机器确认未生效见下文门槛，不能由“整批原子”推断所有失败均已确认回滚。外层 Uid/Version 驱动写入，不能用 Data.Version 覆盖外层版本。

| 场景 | 当前语义与证据 |
| --- | --- |
| HTTP 空批次/损坏 JSON/错误 Guid | 400 VALIDATION，业务端口未调用 |
| 缺任一操作权限 | 403 FORBIDDEN，业务端口未调用 |
| 实际产品外层版本过期、业务校验或写入拒绝 | 服务失败、data=null；当前归为 BUSINESS_BLOCKED，映射 422；补充断言并沿用事务夹具验证。业务拒绝展示不能证明所有同 code 分支未生效 |
| 完整成功 | 200、ServiceResult.success=true、整批 allSucceeded；保存后以 ClientKey/Uid/Version 回执核对草稿 |
| 已分类的序列化终止 | SERVICE_UNAVAILABLE，503；事务夹具覆盖读取/写入/提交阶段及清理故障，不自动重试；不把其他 503 一并视为已确认未生效 |
| 提交结果无法确认 | OUTCOME_UNKNOWN，503；需核实，不能直接接纳草稿或再次提交 |
| 泛型服务行失败 | 可能仍为 HTTP 200、外层 success=true，行 success=false/code=CONCURRENCY_CONFLICT；已有 Customer 服务与 HTTP 回归测试保留此约定 |

HTTP 测试注入 VERSION_CONFLICT 验证全局 409 映射，只证明已有错误出口；不证明真实产品过期版本返回 409，因此产品 OpenAPI 未声明这条业务响应。

本轮事务测试是内存仓储/会话夹具；覆盖失败不留部分写入、提交异常、未知提交及回滚异常等边界，但不证明 PostgreSQL 实例的隔离、锁序或事务效果。5 秒防重复提交是单实例防抖，没有业务幂等键，不等于幂等保存。

<a id="product-recovery"></a>

## 后续产品接入的恢复与错误门槛（2026-10-08 补充，未验收）

提交分类与解锁的规则正文见 [WEB-TAB-02/03 保存契约](Web多标签工作区设计.md#workspace-save)，未知恢复与记录导出见 [WEB-TAB-04 恢复与人工退出](Web多标签工作区设计.md#workspace-recovery)。本节只记录当前 Product 的证据、保守接入规则及缺口，不把待实施规则写成后端已具备的能力。

| 能力/缺口 | Product 当前事实 | 前端必须实现与验证 |
| --- | --- | --- |
| 冻结请求与追踪 | BatchSave 无业务 CommandId/幂等键；X-Correlation-ID 用于追踪 | 保留提交时间、完整冻结意图、ClientKey/Uid/基线 Version 和追踪头；attemptId 只作本地关联 |
| 结果核实 | 没有专用保存结果查询协议 | 用现有授权读取独立核对；读回不 reset 草稿，不凭部分行相等接纳全批 |
| 无法机器核实 | 新建行身份、部分回执、读权不足可能使读回不充分 | 有权限业务人员按现有业务/运维渠道核实；保全记录，明确结论及责任人，不扩权或自动重发 |
| 人工退出 | Product 真实请求的恢复 UI/材料导出尚未接入；公共入口已由模拟验收页消费 | 按 WEB-TAB-04 接真实用例并核验用户主动、当前权限允许的记录导出及两步确认；未保全/无权限/取消不终止，批量关闭不调用；本地释放不表示服务器失败 |
| 版本拒绝 | 422/BUSINESS_BLOCKED 无专用版本冲突标识 | 展示业务拒绝并保留草稿；不能解析中文 message 区分冲突，也不能据此归为 B 类或解锁重提 |
| 未确认回滚分类不完整 | 校验/行写入失败的 BUSINESS_BLOCKED，以及一般异常的 UNEXPECTED，在回滚确认与未确认分支使用相同 code；只在 message 有差异 | 已派发 Product 请求收到 422/BUSINESS_BLOCKED 或 500/UNEXPECTED，当前一律按 C 类保留冻结请求和锁，进入核实；即使文案称已回滚，也不 acceptChanges、不普通放弃、不自动或默认允许重提。取得满足 A/B 的独立证据后才接纳/解锁 |

上述差异可定向核对 [ProductMaintenanceService.BatchSaveAsync](../SwiftExport.Application/Services/Products/Catalog/Maintenance/ProductMaintenanceService.cs)、[事务夹具](../SwiftExport.Application.Tests/MasterData/ProductBomSaveBoundaryTests.cs) 和 [首批 HTTP 测试](../SwiftExport.Application.Tests/WebApi/首批WebHttp契约测试.cs)。这是源码/既有测试断言的只读核对，没有重跑测试，也不证明当前整合或 PostgreSQL 实测通过。BUSINESS_BLOCKED 还包含派发后无法从响应辨识的事务前拒绝；不能用“这次可能尚未写入”替代可靠分类。

**产品真实写入接入前的独立后端契约门槛（未完成）**：提供稳定机器可读语义，能区分本次确认未生效与未确认回滚/提交结果，并覆盖校验、行写失败、一般异常及提交/清理故障组合；专用版本冲突 UX 另需稳定冲突标识。须同步端点契约并完成定向 HTTP、事务及隔离 PostgreSQL 验收，前端再核验完整回执/恢复行为。本轮不指定错误码或 DTO 的实现形式，不修改事务、权限或命令管道。没有 CommandId/结果查询协议的现状仍然保留，不将新增命令管道作为本轮任务。

| 分类验收场景 | 后续通过要求与当前证据边界 |
| --- | --- |
| 确认未发送 | 派发前转换/校验失败或明确未执行发送：B 类，保留包括无效缓冲的输入，解除准备锁，实际请求数为零；网络错误/取消/超时本身不作未发送证据 |
| 已验证短路拒绝 | 仅已测 Product 空批次/损坏 JSON/错误 Guid 的 400/VALIDATION，以及缺操作权限的 403/FORBIDDEN，宿主证明业务端口未调用；feature 核对具体契约后可按 B 类。不能推广到任意端点或同状态码其他场景，权限未恢复仍禁止提交 |
| 无法确认的业务失败 | 当前 Product BUSINESS_BLOCKED/UNEXPECTED 按 C 类，即使展示“业务拒绝”也保留锁；后端门槛须验证回滚确认/未确认可机器区分，而非读取中文 message |
| 序列化终止 | Product SERVICE_UNAVAILABLE 的具体序列化分支有内存夹具断言支持未提交；按 B 类接入前补定向 HTTP/实际 PG 分类及未生效证据。不满足已验证具体契约时按 C 类，不以 503 统一解锁 |
| 响应丢失/未知结果 | 请求已发出无可靠响应、OUTCOME_UNKNOWN 或回执不完整/无法关联：C 类；保留冻结意图/追踪，不重发、不接纳、不普通放弃，恢复不足继续限制 |
| 完整成功 | Product HTTP 200、外层 success、整批 allSucceeded 均满足，逐项核对 ClientKey/Uid/Version、操作及完整覆盖冻结请求；完成接纳后才更新基线/解锁，剩余输入保护仍阻止 saved/关闭 |
| 已有行级部分成功 | 其他业务沿用真实行级契约，仅接纳确认成功行、保留明确失败行；全部行结果确认并处理后可解锁但仍有草稿返回 unsaved，未确认行按 C 类。既有 Customer 回归约定不等于 Web UI 已验收，不改成 Product 原子语义 |

恢复导出取消/失败、未确认、权限不足、合法保全后人工终止及批量关闭不能绕过的验收统一见 [多标签恢复规则](Web多标签工作区设计.md#workspace-recovery) 与该文第 9 节验收矩阵；首期恢复记录的文件去向、权限和确认规则只在该设计维护。

## 典型账号的整页可用性门槛（2026-10-08 补充，未验收）

页面查看权限不等于所有候选读取权限。真实账号、me 投影、每个候选请求、页面可显示信息和允许编辑字段必须一起记录；不为下拉框加载自动授予系统管理或保留权限。

| 典型账号/场景 | 预期页面行为与检查 |
| --- | --- |
| 仅 Products.Product.View | 可打开产品列表；只显示响应已有的分类/颜色/单位信息；不能解析的名称给局部提示，不发无权读取、不把已有值清空 |
| 产品查看 + 合法候选读取键 | 分类/Unit/颜色等显示正常；核对对应权限和响应，仍没有 Create/Edit/Delete 写入入口 |
| 产品编辑且具备所需候选读取 | 按每种操作检查写键；取得允许候选、校验冻结并提交；不把 Edit 当 Create/Delete |
| 有产品写权但缺某候选读取权 | 对依赖该候选的选择/修改局部禁用并说明原因；保留已存值。若完成当前用例必须改变该字段，则阻断该操作，不扩大授权 |
| 没有 Suppliers.Supplier.View | 不查询供应商管理候选；产品上已有供应商信息按响应呈现，禁止相关候选编辑/跨页打开绕权 |
| 活动/隐藏页权限撤销 | 停止相关读取/编辑，保留草稿；失去产品读权遮蔽页面。重新激活/提交也核权，不只改菜单 |
| 候选空数据、停用值、读取失败 | 空集合与 403/业务拒绝分别呈现；已有停用值可说明，不能静默替换；Unit 启用与分类空数据沿用实际服务事实 |

选择器/字段权限只是前端可用性约束，不声明后端已经提供通用字段授权。准备真实账号须使用现有合法授权流程；本轮没有创建账号、修改角色或读写运行库。验收必须附实际 me/请求状态/页面截图或浏览器测试证据，而非只检查按钮可见。

## 验收结果与可复现命令

隔离快照：基准提交 `448819d346d426e43f0f0d392923f291d1b8f4f2`，加下文列出的六个本轮源码文件。清单含 SHA-256，位于 `_tools/Web契约验证/.artifacts/验收快照清单.json`，逐项核对与本轮源码一致。以下测试分组计数来自该快照；另外列出本轮结束前的共享工作区组合复查。

| 验证 | 结果 |
| --- | --- |
| 新增 FirstWebHttpContractTests | 19 通过 |
| ProductBomSaveBoundaryTests | 61 通过 |
| AuthService / EndpointPermissionFilter / EndpointPermissionHttp | 10 / 13 / 1 通过 |
| HttpErrorContract / JwtAndControllerContract / PermissionCatalogConsistency | 33 / 3 / 2 通过 |
| UpdateIdentity / CustomerUpdateConcurrency / ProductDefaultWarehouseCandidate | 6 / 5 / 2 通过 |
| 定向测试合计 | **155 通过，0 失败，0 跳过** |
| 后端解决方案隔离构建 | **0 警告，0 错误** |
| 共享工作区后端解决方案构建 | **成功，7 个既有 MSTest 分析器警告，0 错误** |
| 共享工作区组合测试 | **154 通过、1 失败**；新增 19 项 HTTP 测试全部通过；失败为并行 BOM 增加路由后旧数量断言未更新 |
| OpenAPI 引用核对 | **9 个端点、30 个组件** |
| Orval 生成、tsc --noEmit、Fetch 验收 | **全部通过** |

HTTP 宿主清空 appsettings/环境配置，关闭开发身份、文件服务和上传，并替换连接工厂为拒绝调用的代理；业务服务只提供场景输入输出。没有连接业务库。Fetch 请求验收使用模拟 fetch，没有正式服务器/浏览器网络调用。

正常组合工作区的复现入口：

```powershell
& .\_tools\Web契约验证\验证契约.ps1
# 已安装锁文件依赖时可加 -SkipNpmInstall。
```

本轮隔离快照实际执行：

```powershell
# 在 .artifacts/验收源码 中执行；该目录来自 git archive HEAD，再覆盖六个本轮源码文件。
$env:NUGET_SCRATCH = 'D:\Documents\source\CSharp\SwiftExport\_tools\Web契约验证\.artifacts\隔离NuGetScratch'
$env:SWIFTEXPORT_WEB_OPENAPI_OUTPUT = 'D:\Documents\source\CSharp\SwiftExport\_tools\Web契约验证\.artifacts\OpenAPI完整.json'
$filter = 'FullyQualifiedName~FirstWebHttpContractTests|FullyQualifiedName~ProductBomSaveBoundaryTests|FullyQualifiedName~UpdateIdentityTests|FullyQualifiedName~CustomerUpdateConcurrencyTests|FullyQualifiedName~EndpointPermissionFilterTests|FullyQualifiedName~EndpointPermissionHttpTests|FullyQualifiedName~HttpErrorContractTests|FullyQualifiedName~JwtAndControllerContractTests|FullyQualifiedName~AuthServiceTests|FullyQualifiedName~PermissionCatalogConsistencyTests|FullyQualifiedName~ProductDefaultWarehouseCandidateTests'
dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --artifacts-path 'D:\Documents\source\CSharp\SwiftExport\_tools\Web契约验证\.artifacts\隔离构建' --filter $filter -p:NuGetAudit=false --logger 'trx;LogFileName=Web契约隔离验收.trx' --results-directory 'D:\Documents\source\CSharp\SwiftExport\_tools\Web契约验证\.artifacts\TestResults' -v:q
dotnet build SwiftExport-WebAPI.slnx --artifacts-path 'D:\Documents\source\CSharp\SwiftExport\_tools\Web契约验证\.artifacts\隔离构建' -p:NuGetAudit=false -v:q
# 在仓库 _tools/Web契约验证 中执行：
npm.cmd run verify
```

组合复查实际执行 `dotnet build SwiftExport-WebAPI.slnx --artifacts-path '_tools/Web契约验证/.artifacts/组合构建' -p:NuGetAudit=false -v:q`，以及 `验证契约.ps1 -SkipNpmInstall`。前者构建成功，后者因下述权限路由数量断言失败而终止；该轮 HTTP 测试仍导出当前共享工作区全量 OpenAPI，随后单独执行 `npm.cmd run verify` 再次通过。提交的首批 OpenAPI 与 client 最终来自这次最新导出。

机器本地证据包括基线/最终全量 OpenAPI、TRX、隔离快照清单；`.artifacts` 不纳入 Git。选出的 `openapi.json`、固定配置/锁文件、生成 client 和调用样例保留在工具目录，便于审查与重新生成。

## 本轮文件归属

六个源码文件：

- `SwiftExport.WebApi/Configuration/WebApiConfiguration.cs`：描述配置与序列化注释。
- `SwiftExport.WebApi/SwiftExport.WebApi.csproj`：匹配现有 Swashbuckle 版本的 Newtonsoft 描述包。
- `SwiftExport.WebApi/OpenApi/首批Web契约描述.cs`：端点描述及泛型组件命名。
- `SwiftExport.Application.Tests/WebApi/首批Web契约宿主.cs`：隔离 HTTP 宿主。
- `SwiftExport.Application.Tests/WebApi/首批WebHttp契约测试.cs`：HTTP 与实际 OpenAPI 验收。
- `SwiftExport.Application.Tests/MasterData/ProductBomSaveBoundaryTests.cs`：只追加产品过期版本错误码/data 两项断言。

工具目录新增 `.gitignore`、`package.json`、`package-lock.json`、`tsconfig.json`、`orval.config.mjs`、`核对首批OpenAPI.mjs`、`openapi.json`、`generated/client.ts`、`产品调用样例.ts`、`Fetch请求验收.ts`、`验证契约.ps1`、`使用说明.md`；文档新增本文件。

2026-10-07 共享工作区另有并行 BOM 任务，本次契约实现没有覆盖这些修改。期间 BOM 命名空间/类型碰撞出现 CS0118 及仓储接口实现错误；该次结束前编译错误已消失。该次组合测试的 `EndpointPermissionFilterTests.EveryRegisteredSingleKey_RequiresItsExactPermission` 失败：断言 232，实际 235。原因是新增 `BomController.SearchProducts`、`BomController.GetNumberInfo`、`ProductController.BindBom` 三条权限路由。以上保留为历史事实；2026-10-08 本次静态核对看到断言已为 235，未重跑测试。最终整合门禁必须重新验证，不能用数量已改或旧隔离通过代替。

## 尚需业务决定或后续验证

- 产品业务/版本失败仍折叠为 BUSINESS_BLOCKED，无法仅凭 code 区分版本冲突。是否调整为专用错误码需要业务授权，本轮不改变语义。
- 部分失败后的回滚本身无法确认时，现有出口仍可能是 BUSINESS_BLOCKED/UNEXPECTED，并在 message 提示未确认；OUTCOME_UNKNOWN 尚未覆盖所有这类组合。报告保留差异，不将目标描述当作已经实现。
- Unit 查询只排除删除项，服务不自动排除 IsEnabled=false；页面选择规则需遵循启用标记。分类无记录的服务现状可能返回 BUSINESS_BLOCKED，而非空成功列表。
- 请求取消只验证 Fetch 信号传递；产品读写服务当前没有贯穿数据库的 CancellationToken 验收。
- PostgreSQL 真实并发/隔离、运行数据库权限投影、实际基础数据内容、文件读取配置、真实浏览器与同源托管均未验收。
- 一些既有 skill 引用旧 Middleware 路径或旧“失败返回 200”表述；实际管道与本报告的 HTTP/行级语义优先。未借本轮改写治理文档。
- 既有字典 DTO 存在中文注释乱码，本轮未扩写或改变其编码；Git 的 LF→CRLF 提示来自工作区换行配置。本轮没有引入业务连接字符串；测试签名键仅供隔离宿主使用。

首批契约与客户端产物已建立；2026-10-07 组合记录有 1 项失败，当前整合验收状态未由本次文档任务重新证明。后续在最终工作树/提交范围重跑组合入口，记录当前版本、实际通过/失败及范围；工程基座、典型账号整页、恢复路径和真实环境验收分别完成后，才能声明对应阶段通过。
