# Web 首批接口契约验收

验收日期：2026-10-07。目标是为后续产品 Web 页面建立可生成、可调用、可检查的 HTTP 契约；本轮没有开发业务前端。

## 范围与实现

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

真实 `ProductMaintenanceService` 使用最外层 Serializable 事务，对整批校验及写入。失败回滚后不回传可接纳的部分成功行；成功才返回完整整批回执。外层 Uid/Version 驱动写入，不能用 Data.Version 覆盖外层版本。

| 场景 | 当前语义与证据 |
| --- | --- |
| HTTP 空批次/损坏 JSON/错误 Guid | 400 VALIDATION，业务端口未调用 |
| 缺任一操作权限 | 403 FORBIDDEN，业务端口未调用 |
| 实际产品外层版本过期、业务校验或写入拒绝 | 服务失败、data=null；当前归为 BUSINESS_BLOCKED，映射 422；补充断言并沿用事务夹具验证 |
| 完整成功 | 200、ServiceResult.success=true、整批 allSucceeded；保存后以 ClientKey/Uid/Version 回执核对草稿 |
| 序列化失败/可判定的事务失败 | SERVICE_UNAVAILABLE，503；不自动重试 |
| 提交结果无法确认 | OUTCOME_UNKNOWN，503；需核实，不能直接接纳草稿或再次提交 |
| 泛型服务行失败 | 可能仍为 HTTP 200、外层 success=true，行 success=false/code=CONCURRENCY_CONFLICT；已有 Customer 服务与 HTTP 回归测试保留此约定 |

HTTP 测试注入 VERSION_CONFLICT 验证全局 409 映射，只证明已有错误出口；不证明真实产品过期版本返回 409，因此产品 OpenAPI 未声明这条业务响应。

本轮事务测试是内存仓储/会话夹具；覆盖失败不留部分写入、提交异常、未知提交及回滚异常等边界，但不证明 PostgreSQL 实例的隔离、锁序或事务效果。5 秒防重复提交是单实例防抖，没有业务幂等键，不等于幂等保存。

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

共享工作区另有并行 BOM 任务的源码改动，本轮没有覆盖这些修改。期间 BOM 命名空间/类型碰撞出现 CS0118 及仓储接口实现错误；本轮结束前这些编译错误已消失。组合测试尚有 `EndpointPermissionFilterTests.EveryRegisteredSingleKey_RequiresItsExactPermission` 失败：断言 232，实际 235。并行任务新增 `BomController.SearchProducts`、`BomController.GetNumberInfo`、`ProductController.BindBom` 三条权限路由，数量断言尚未同步。本轮没有调整或删除其断言。隔离 155 项通过与共享 154 项通过不能替代剩余的组合门禁。

## 尚需业务决定或后续验证

- 产品业务/版本失败仍折叠为 BUSINESS_BLOCKED，无法仅凭 code 区分版本冲突。是否调整为专用错误码需要业务授权，本轮不改变语义。
- 部分失败后的回滚本身无法确认时，现有出口仍可能是 BUSINESS_BLOCKED/UNEXPECTED，并在 message 提示未确认；OUTCOME_UNKNOWN 尚未覆盖所有这类组合。报告保留差异，不将目标描述当作已经实现。
- Unit 查询只排除删除项，服务不自动排除 IsEnabled=false；页面选择规则需遵循启用标记。分类无记录的服务现状可能返回 BUSINESS_BLOCKED，而非空成功列表。
- 请求取消只验证 Fetch 信号传递；产品读写服务当前没有贯穿数据库的 CancellationToken 验收。
- PostgreSQL 真实并发/隔离、运行数据库权限投影、实际基础数据内容、文件读取配置、真实浏览器与同源托管均未验收。
- 一些既有 skill 引用旧 Middleware 路径或旧“失败返回 200”表述；实际管道与本报告的 HTTP/行级语义优先。未借本轮改写治理文档。
- 既有字典 DTO 存在中文注释乱码，本轮未扩写或改变其编码；Git 的 LF→CRLF 提示来自工作区换行配置。本轮没有引入业务连接字符串；测试签名键仅供隔离宿主使用。

首批契约与客户端产物已建立；组合验收仍有上述 1 项门禁未通过。并行任务完成权限回归后，应重新运行组合验证入口，再实施业务 Web 页面与实际环境验收。
