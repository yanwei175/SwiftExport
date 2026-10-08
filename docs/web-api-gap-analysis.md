# Web API 差距与前置工作（状态跟踪）

原审计：2026-10-07；状态同步：2026-10-08。与[可用性盘点](web-readiness-audit.md)、[权限设计](web-permission-design.md)联合使用。本文区分已关闭问题、待验证疑点和按用例接入条件，不实施修复，也不是全部后端整改任务书。

## 1. 当前结论与已关闭事项

1. **首批九个端点契约已验收。** 已补 Newtonsoft/schema、成功/错误/JWT 声明，并完成固定 Orval Fetch 生成和 strict 检查。其余接口特别是文件仍按真实页面需求验收，不能泛称全部 OpenAPI 已完整。
2. **前端接入方案已明确，部署尚未验收。** 独立静态 Web + 同源反向代理；开发 Vite proxy；内存 Bearer + me，过期同用户重认证。该方案无需先新增全局 CORS，实际网络/文件验收另做。
3. **身份、Version 和五端点登记问题已修复。** API-03/05/20 不再发整改任务。产品过期版本当前为 422/BUSINESS_BLOCKED，HTTP 200 内层失败也须检查；这些已明确的协议不因偏好 409 自动成为待修 Bug。

依据 [首批接口验收](Web首批接口契约验收.md) 与 [_tools 验证说明](../_tools/Web契约验证/使用说明.md)：隔离验收报告 155 项通过，不写数据库。报告中的共享区权限数量断言失败属于当时并行状态；本次仅复核该数量断言测试 1 项通过，不声称共享区全套重跑。真实 PG 并发、文件及实际浏览器部署仍未覆盖。

依据：[WebApiConfiguration](../SwiftExport.WebApi/Configuration/WebApiConfiguration.cs)、[BaseController](../SwiftExport.WebApi/Controllers/Base/BaseController.cs)、[EndpointPermissionFilter](../SwiftExport.WebApi/Authentication/EndpointPermissionFilter.cs)、[ProductMaintenanceService](../SwiftExport.Application/Services/Products/Catalog/Maintenance/ProductMaintenanceService.cs)。

## 2. API一致性分类

| ID | 分类 / 范围 | 已确认事实与影响 | 最小后续动作 / 验收 |
| --- | --- | --- | --- |
| API-01 | Can Keep，全局 | api/[controller]、api/sales-orders、api/system等风格并存；LowercaseUrls配置不使历史模板自动统一。浏览器均能消费 | 生成器采用真实paths；新路由命名稳定，不批量改旧URL |
| API-02 | Can Keep，查询/命令 | POST page/search与GET售后筛选并存；POST批量、confirm/post/reverse有业务含义 | adapter使用真实method，不为REST机械重写；查询虽POST仍可用useQuery |
| API-03 | 已关闭 | 路由 id 与 body.Uid 一致性校验已补 | 保留原请求包装，不重复修复；新入口继续核对身份 |
| API-04 | 待 HTTP 验证，采用单项 Delete 前 | 旧复杂 DeleteItemRequest 参数和 Guid 路径存在静态绑定疑点；尚无本轮真实 MVC 复现 | 先验证实际 path/body/query 与调用方，确认失败才定修复；Product 使用已验收 DeleteItems |
| API-05 | 已关闭 | BaseService 映射后显式赋包装 Uid/Version | 不重复整改；各新接入模块仍验证自己的并发协议 |
| API-06 | Can Keep，分页shape | PagedRequestBase有PageIndex=1/PageSize=20/Keyword；PagedResponseBase是Items/TotalCount/PageIndex/PageSize/TotalPages，不叫PagedResult | Table分页从实际响应读取，生成器用现有类型 |
| API-07 | Should Fix，参数策略 | Product仓储归一化页码、最大500；RBAC服务验证1..200；AfterSalesSearchRequest没有属性默认值；候选默认页大小不同 | 文档明确默认/上限，前端总是发送合法值；不是强制所有模块同一上限 |
| API-08 | Can Keep，强类型筛选 | ProductSearchRequest的分类/颜色/客户集合，销售/采购的日期状态条件已存在；没有公开通用Filter DSL | feature映射条件；禁发字段名/SQL表达式；只有真实需求才加通用规则 |
| API-09 | Should Fix，排序/搜索UX | 没有统一排序请求；Keyword/BatchKeywords/Keywords/QuickView含义不同 | 当前Table不展示无法全局生效的服务端排序；专用DTO名称可保留，新增白名单排序按模块实施 |
| API-10 | Can Keep，批量shape | CreateItems/UpdateItems/DeleteItems；ClientKey/RowIndex关联；Version在Update/Delete包装；Computed TotalCount/HasChanges是只读 | 不把createdItems直接作为body；以mapper构建DTO，忽略只读计算字段 |
| API-11 | 首批协议已明确；其他模块按需验收 | Product stale Version 是 422/BUSINESS_BLOCKED，原子批次失败不接纳；通用 CRUD 可 HTTP200 外层成功但行失败。旧 Supplier 错误分类风险不代表全仓已统一或已验收 | 消费实际两层协议；不以 409 为 Product 开工前提。需要结构化冲突专门 UX 时另立契约需求；不解析中文 Message 猜类型 |
| API-12 | Should Fix，业务/Validation | Fail(message)默认BUSINESS_BLOCKED→422，旧输入校验、缺失对象也使用此重载；ModelState只回首个error.Field | 新触碰用例区分VALIDATION/NOT_FOUND/STATE_BLOCKED；稳定码分支，不解析中文Message；错误汇总按真实表单需求 |
| API-13 | Can Keep，HTTP错误出口 | ModelState400、JWT401/403、StatusCodePages补401/403/404；ServiceErrorHttpMapper覆盖409/422/429/503；未知码500且脱敏 | 保留ServiceResult错误协议，无需另造ProblemDetails；补OpenAPI声明而非第二套runtime响应 |
| API-14 | Can Keep，成功shape例外 | auth/login返回LoginTokenDto，auth/me返回MeDto，其他多数ServiceResult，download二进制，HEAD无正文 | transport按契约解析，不统一把所有成功响应当data字段 |
| API-15 | 首批已关闭；新增契约按需验证 | 已接 Newtonsoft schema 支持，首批枚举样本与生成结果已对照 | 后续新增字段继续查实际 JSON；不改变数据库整数枚举语义 |
| API-16 | 首批已验收；其余 DTO 按需核对 | 首批 nullable/required/集合已完成 schema 与严格类型验证；Contracts 仍保留 netstandard2.0 | 新接入模型核对 null/缺省/必填集合，不能宣称全仓 DTO 已完整 |
| API-17 | Should Fix，取消 | 273条声明中110条签名无CancellationToken（含继承和HTTP计数）；现代单据多有token，手工来源及旧资料CRUD缺；有token不代表仓储全链传递 | 按新触碰查询传递RequestAborted，避免浏览器取消仍耗资源；已提交写入取消不能视作回滚 |
| API-18 | Must Fix，跨日期/时区用例前 | BaseDto用DateTime?，BaseService有DateTime.Now，PG参数显式区分timestamp与timestamptz；无全局业务日期语义约定 | 列字段矩阵：业务日期/本地墙上时间/UTC时刻；未分清不能统一toISOString或给旧值补Z |
| API-19 | Can Keep，Guid | Guid路径/DTO与PG uuid可映射TS string；通用仓储新增会覆盖Uid生成新值 | 浏览器crypto.randomUUID用于ClientKey/临时技术身份；必须以响应Uid重映射，不能要求复制旧顺序Guid算法 |
| API-20 | 已关闭 | 采购关联四动作登记 Procurement.Order.Edit；采购 Excel Check 登记 Procurement.Order.Create | 不重复修复；默认拒绝继续保留；后续功能联调检查真实键 |
| API-21 | Can Keep，文件政策 | FileAccessPolicy活动用户可读、Admin可写；端点还要求System.Files.Manage；PermissionCatalog有保留权限 | 明确UI按真实双层条件，不推断普通用户获键即可上传；非管理员上传需单独业务授权 |
| API-22 | Should Fix，旧Refit契约 | IGenericApi的TDto/Guid响应与BaseController不一致；IProductApi.GetCategoryTreeAsync没有实际路径 | 核查实际调用方后修兼容；TS从OpenAPI生成，禁止以Refit作为唯一协议事实 |

具体代码：
[BaseService](../SwiftExport.Application/Services/Base/BaseService.cs)、[Product单项重写](../SwiftExport.Application/Services/Products/Catalog/Maintenance/ProductMaintenanceService.SaveBoundary.cs)、[SupplierService](../SwiftExport.Application/Services/Suppliers/Management/SupplierService.cs)、[CategoryService](../SwiftExport.Application/Services/Products/Categories/ProductCategoryService.cs)、[SaveItemResultFactory](../SwiftExport.Contracts/Common/SaveItemResultFactory.cs)、[ServiceResultHttpFilter](../SwiftExport.WebApi/ErrorHandling/ServiceResultHttpFilter.cs)、[ServiceResult](../SwiftExport.Contracts/Wrappers/ServiceResult.cs)、[时间参数](../SwiftExport.Infrastructure/Repositories/Base/PostgreSqlParameters.cs)、[通用仓储](../SwiftExport.Infrastructure/Repositories/Base/PostgreSqlDapperRepository.cs)、[FileAccessPolicy](../SwiftExport.Application/Services/Files/FileAccessPolicy.cs)。

表中静态计数与未重新验收的模块描述来自初次快照；不是当前全量复扫结果。已关闭项不再修，待验证项先复现，条件项只在采用对应用例时处理；本轮不修改代码。

## 3. OpenAPI / Swagger专项：初次检查表与后续覆盖

下表保留初次静态审计的检查点，不代表当前仍无元数据。随后已补 Swashbuckle Newtonsoft 支持、稳定 schema/operation ID、首批响应/认证/错误与 nullable 声明；运行生成和客户端验证见首批报告。尚未覆盖文件和所有未来业务 DTO。生产仍不因生成客户端而强开 Swagger UI。

| 检查点 | 当前覆盖 / 初次风险 | 后续动作 |
| --- | --- | --- |
| Endpoint覆盖 | 首批九端点/30组件已导出并验收；五权限路径已登记；273 是原静态快照 | 新页面按 MVC descriptor/真实 paths 核对，不把静态数量当运行成功率 |
| DTO schema | 首批 login/me 裸 DTO、其他包装成功类型已声明 | 新接入 IActionResult 端点明确实际正文，不批量改变成功 shape |
| Enum | Newtonsoft 支持已接线，首批字符串枚举已对照 HTTP 与生成结果 | 后续枚举新增继续样本对照，不改变存储整数语义 |
| Nullable/required | 首批 null/集合/必填语义已验证；全仓旧参考类型未统一验收 | 按实际 DTO 验证缺省/null，strict 编译，不以 nullable 等同可省略 |
| 泛型、分页、BatchSave | 首批分页/批量包装已生成并严格编译 | 后续闭合泛型用同一策略，响应完整性仍由 feature 检查 |
| schema ID | 初次隔离导出复现泛型组件名违规，现已稳定命名并验证 | 保留全量引用/组件检查；不再派首次修复任务 |
| operationId/tag | 首批稳定 operationId 已补 | 新端点核对唯一/稳定；无需重命名既有 URL |
| 统一错误 | 实际ServiceResult<object>.Fail经Filter/中间件输出；不是ProblemDetails | 描述400/401/403/404/409/422/429/500/503与error字段，413/415按上传；定义default错误响应 |
| JWT | Bearer 定义和首批 requirement 已补，login 匿名、me 受保护已验收 | 后续端点按实际身份标注；不能宣称全仓 security 已核对 |
| 上传原始流 | PUT files/{containerId}只在内部读Request.Body，没有显式body参数 | requestBody application/octet-stream、binary；path/overwrite为query |
| multipart | ProductImageController手动MultipartReader只接收重复files节，无IFormFile参数 | requestBody multipart/form-data对象files=array(binary)，写清逐项响应；不能只加Consumes就认为body已导出 |
| 下载/HEAD | IActionResult返回File流，enableRangeProcessing=true；无成功内容声明 | GET 200/206 binary、HEAD无body；描述Range/Content-Range、Content-Disposition及416 |
| 生产Swagger | Development分支发布，生产不公开 | CI离线导出schema或隔离Development宿主；不为生成器强开生产UI |

依据 [Swashbuckle 官方说明](https://github.com/domaindrivendev/Swashbuckle.AspNetCore/blob/master/README.md) 与 [OpenAPI 组件规范](https://spec.openapis.org/oas/v3.0.3.html#components-object)；本项目实际复现/修复证据见首批报告。只报告覆盖范围，不扩写成所有泛型或所有接口均已验收。

**当前未验收范围**：文件原始流/手动 multipart/下载、模糊单项 DELETE 及首批之外所需运行错误。Auth login/me 与首批 page/batch-save 已有声明和运行生成证据，不再列为未声明。

后续保留 Swashbuckle，按待接入端点补描述 → 导出并核对 schema/$ref → 同一 Orval 生成编译 → HTTP 样本对照 → 固定产物。首批 Product/auth 已完成此路径；文件在实际采用时补齐，不切第二套服务器生成器。

## 4. TypeScript Client方案比较

| 方案 | 与项目的适配判断 | 采用建议 |
| --- | --- | --- |
| NSwag | .NET团队友好，可从OpenAPI生成TS Fetch client；便于msbuild工具链但增加另一套生成配置；没有自动解决BatchSave事务/错误或Dirty Tracking | 备选。只作client生成可继续吃现有Swagger；无需替换server |
| Kiota | 支持TS，提供request adapter/auth/serialization，适合多语言SDK；运行依赖和请求构建层对单个ERP SPA较重 | 暂不选；已有统一transport无需加SDK框架 |
| Orval | 支持Fetch与TanStack Query输出、自定义mutator；可生成操作函数和类型，节省大量API重复代码 | **首选：先生成Fetch函数+DTO**，feature自己维护查询key/mutation失效；规模增加时再选择生成query hooks |
| openapi-typescript | 生成纯类型；配openapi-fetch可获得类型安全路径/入参/返回，轻量；并非单独生成具名API函数 | 简洁备选，若PoC发现Orval输出不便维护可选此单一路径，不同时保留两套client |
| OpenAPI Generator | typescript-fetch成熟、可定制、适合跨语言SDK；CLI/JAR或容器与模板维护成本较高 | 暂不选；没有当前必须跨语言分发SDK的需求 |

官方依据：[NSwag TS生成](https://github.com/RicoSuter/NSwag/wiki/TypeScriptClientGenerator)、[Kiota TS](https://learn.microsoft.com/en-us/openapi/kiota/quickstarts/typescript)、[Orval配置](https://orval.dev/docs/reference/configuration/output/)、[openapi-typescript](https://openapi-ts.dev/introduction)、[openapi-fetch](https://openapi-ts.dev/openapi-fetch/)、[OpenAPI Generator typescript-fetch](https://openapi-generator.tech/docs/generators/typescript-fetch/)（2026-10-07查询）。推荐是结合本项目专用POST查询、ServiceResult与BatchSave的判断，不是生成器性能排名。

无论选哪种：生成DTO和HTTP操作，不生成业务判断、权限授权或编辑状态。固定生成器版本和schema快照，generated不可手改；Fetch transport统一token、非2xx错误、correlation、AbortSignal、Blob，但不更改端点的成功shape。BatchSave mapper在feature/useEditableCollection边界，不在generator模板或Grid里。

## 5. P0 / P1 / P2前置任务

| 优先级 | 事项 / 条件 | 交付与验收标准 |
| --- | --- | --- |
| P0（首批已验收） | OpenAPI 完整性 | 九端点已有隔离 HTTP/schema/Orval/strict 证据；后续 feature 只补自己需要的契约，不重复修首批 |
| P0 | 浏览器网络基线 | 推荐生产同origin反向代理/api和图片，开发Vite proxy；若直连不同origin则明确AddCors/UseCors、OPTIONS与允许origin/header/method；现在未实现CORS |
| P0 | JWT使用 | 现有Bearer可用；明确过期401保留草稿、重新登录，首次me加载及权限；DevUser关闭场景验证；不要求先上Refresh Token |
| P0（前端消费与联调） | 错误与并发 | Product 当前 stale Version 为 422/BUSINESS_BLOCKED；检查两层 success、原子回执、OUTCOME_UNKNOWN；不盲重试。真实 PG 并发另验收 |
| P0 | DTO数据含义 | Guid临时到持久Uid映射、null/optional、enum、业务日期/时刻；金钱输入保留十进制文本并在boundary转现有decimal JSON，金额/舍入仍服务端权威 |
| 已关闭 | 五端点权限登记 | 已登记精确键，不再派重复修复任务；模块联调仍检查授权 |
| P1 | 文件与图片 | 在使用上传/下载时补schema、Blob授权读取、公共图片URL；已有Range/HEAD/大小限制，验收代理大小/超时与Header读取；业务附件列表/归属尚未发现 |
| P1 | 权限/会话体验 | 复用me，不重复建current-user-permissions；登出清Query cache，403刷新me；角色变更不会自动刷新客户端快照，重新读取策略需实现 |
| P1 | 编辑/批量保存 | SwiftDataGrid基础、useEditableCollection、未保存导航、保存中禁止并发编辑、按模块原子性处理SaveItemResult、保留失败草稿 |
| P1 | 测试/CI/部署 | DTO生成差异、类型检查、lint、针对性单元+HTTP mock+浏览器闭环；静态部署SPA fallback、hash assets缓存、入口无长缓存、HTTPS |
| P1 | 日志/审计 | 使用X-Correlation-ID和已有CommandId/BusinessOperationLog；记录错误类别和用例，脱敏；按需要建立审计查看，不新增第三套审计存储 |
| P1（有实际需求） | 远程排序/查询取消 | 仅给实际表格支持白名单排序；新触碰旧资料查询补全token链；不要默认通用DSL |
| P2 | Refresh Token/记住登录 | 出现长会话明确需求再设计撤销、轮换、过期与多端策略；当前无refresh接口，不可让前端模拟刷新 |
| P2 | HttpOnly Cookie/BFF | 若改Cookie须评估CSRF/SameSite、凭据CORS及WPF并存；不是当前Bearer PoC必需 |
| P2 | SignalR/WebSocket | 当前查询+保存失效已足够；有实时多用户进度/通知需求再引入 |
| P2 | 大文件扩展 | Range下载已有；上传断点续传、直传对象存储、分片签名均未确认已有，不在普通ERP PoC前扩建 |
| P2 | 国际化/复杂Grid | 多语言业务确认后引react-i18next；首期中文locale；跨页编辑、范围选择、撤销栈按证据逐步引入 |

### 认证方案具体选择

PoC先用现有Bearer，访问token保存在会话内存；刷新页面需重新登录是该最小方案的已知体验。若业务必须刷新后维持会话，需先明确存储策略，不能偷偷加入localStorage。HttpOnly Cookie+BFF可以后续采用，但它会增加服务端会话和CSRF责任，不应作为“开发Web前必须大重构”。当前12小时JWT不是Refresh Token；过期只重认证，不能生成新CommandId自动重发原写入。

浏览器原生链接无法附自定义Authorization；受保护下载需fetch→Blob→object URL→释放，公共图片可直接img；不能将token拼进query。跨origin要Expose-Headers包含X-Correlation-ID、Content-Disposition、Content-Range、Retry-After等按实际需求，Range请求也须验证；同源无需此CORS暴露。

### 完成标准

完成P0意味着所选PoC闭环有证据：匿名登录、me、无权403、列表分页/搜索、批量建改删、陈旧版本拒绝、结果不明保留草稿、重新登录恢复流程、schema生成编译通过。不是“六份规划文档写完即后端已达标”。

## 6. 待确认与验证证据

首批已验证运行 Swagger、Orval Fetch 生成和 strict 类型检查，见报告；尚未验证实际浏览器部署/文件、真实 PG 并发或所有业务组合。未扫描或公布配置密钥值。部署代理/缓存、运行库历史授权表、MainImage 地址可用性仍不能由源码推定。

未来最小验证可复用[HttpErrorContractTests](../SwiftExport.Application.Tests/Authentication/HttpErrorContractTests.cs)、[JwtAndControllerContractTests](../SwiftExport.Application.Tests/Authentication/JwtAndControllerContractTests.cs)、[EndpointPermissionHttpTests](../SwiftExport.Application.Tests/Authentication/EndpointPermissionHttpTests.cs)、[FileApiIntegrationTests](../SwiftExport.Application.Tests/Files/FileApiIntegrationTests.cs)。真实PG写测试须另有明确实例/库/数据与清理授权，文档规划不构成数据库写入授权。

