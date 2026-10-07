# Web API 差距与前置工作（草案）

日期：2026-10-07。与[可用性盘点](web-readiness-audit.md)、[权限设计](web-permission-design.md)联合使用。本文给出未来任务，不实施修复；Must Fix是满足对应Web用例的条件，不要求全部模块在Product PoC前改完。

## 1. 三个主要阻碍

1. **OpenAPI与实际JSON/HTTP契约没有完整对齐。** Newtonsoft枚举、Auth的IActionResult、错误Filter、手动上传/下载都缺生成所需的元数据。
2. **浏览器接入缺明确基线。** 没有CORS接线，Bearer会话存储/过期策略、文件请求和图片origin尚须选择、验证；同源代理可避免生产跨域需求。
3. **写入结果与授权接线存在具体缺口。** 通用CRUD路径身份和Version有歧义、部分并发错误丢失分类、5个新端点被默认拒绝。必须按功能处理，不能用按钮隐藏或自动重试掩盖。

依据：[WebApiConfiguration](../SwiftExport.WebApi/Configuration/WebApiConfiguration.cs)、[BaseController](../SwiftExport.WebApi/Controllers/Base/BaseController.cs)、[EndpointPermissionFilter](../SwiftExport.WebApi/Authentication/EndpointPermissionFilter.cs)、[ProductMaintenanceService](../SwiftExport.Application/Services/Products/Catalog/Maintenance/ProductMaintenanceService.cs)。

## 2. API一致性分类

| ID | 分类 / 范围 | 已确认事实与影响 | 最小后续动作 / 验收 |
| --- | --- | --- | --- |
| API-01 | Can Keep，全局 | api/[controller]、api/sales-orders、api/system等风格并存；LowercaseUrls配置不使历史模板自动统一。浏览器均能消费 | 生成器采用真实paths；新路由命名稳定，不批量改旧URL |
| API-02 | Can Keep，查询/命令 | POST page/search与GET售后筛选并存；POST批量、confirm/post/reverse有业务含义 | adapter使用真实method，不为REST机械重写；查询虽POST仍可用useQuery |
| API-03 | Must Fix，采用单项Update前 | BaseController.Update、ProductController.Update、SupplierProductController.Update等接收路由Guid id但只转发body.Uid，未比对两者；路径A可以实际修改B | 比对路由与body身份并拒绝不一致，保留既有请求包装；PoC先走batch-save |
| API-04 | Must Fix，采用单项Delete前 | `DELETE {id}`的参数是复杂DeleteItemRequest id，没有明确FromBody/FromQuery；ApiController复杂绑定推断与路径Guid不一致，Refit泛型又只发Guid路径 | 明确Guid路由及版本来源，兼容已有调用方；用真实MVC验证path/body/query与生成client；PoC用DeleteItems |
| API-05 | Must Fix，使用BaseService单项Update前 | BaseService.UpdateAsync按item.Uid加载实体后item.Data.Adapt(existing)，未将item.Version显式赋给entity.Version；包装Version与Data.Version可能不一致，实际依据取决于映射 | 统一以包装身份/预期Version为权威，验证陈旧请求必冲突。Product已有SaveBoundary重写，勿将此问题泛化到所有Product写入 |
| API-06 | Can Keep，分页shape | PagedRequestBase有PageIndex=1/PageSize=20/Keyword；PagedResponseBase是Items/TotalCount/PageIndex/PageSize/TotalPages，不叫PagedResult | Table分页从实际响应读取，生成器用现有类型 |
| API-07 | Should Fix，参数策略 | Product仓储归一化页码、最大500；RBAC服务验证1..200；AfterSalesSearchRequest没有属性默认值；候选默认页大小不同 | 文档明确默认/上限，前端总是发送合法值；不是强制所有模块同一上限 |
| API-08 | Can Keep，强类型筛选 | ProductSearchRequest的分类/颜色/客户集合，销售/采购的日期状态条件已存在；没有公开通用Filter DSL | feature映射条件；禁发字段名/SQL表达式；只有真实需求才加通用规则 |
| API-09 | Should Fix，排序/搜索UX | 没有统一排序请求；Keyword/BatchKeywords/Keywords/QuickView含义不同 | 当前Table不展示无法全局生效的服务端排序；专用DTO名称可保留，新增白名单排序按模块实施 |
| API-10 | Can Keep，批量shape | CreateItems/UpdateItems/DeleteItems；ClientKey/RowIndex关联；Version在Update/Delete包装；Computed TotalCount/HasChanges是只读 | 不把createdItems直接作为body；以mapper构建DTO，忽略只读计算字段 |
| API-11 | Must Fix，首批写入模块 | BaseService可能ServiceResult.Success=true而SaveItemResult.Success=false；HTTP Filter只看外层Success，内层冲突仍可能200。Product/Category整批失败返回外层Fail但经纯Message分支转为BUSINESS_BLOCKED/422；Supplier批量写冲突异常转UNEXPECTED/500 | 为选定PoC端点保证VERSION_CONFLICT/409或明确的逐项协议；前端检查两层结果。不能宣称全仓409已统一 |
| API-12 | Should Fix，业务/Validation | Fail(message)默认BUSINESS_BLOCKED→422，旧输入校验、缺失对象也使用此重载；ModelState只回首个error.Field | 新触碰用例区分VALIDATION/NOT_FOUND/STATE_BLOCKED；稳定码分支，不解析中文Message；错误汇总按真实表单需求 |
| API-13 | Can Keep，HTTP错误出口 | ModelState400、JWT401/403、StatusCodePages补401/403/404；ServiceErrorHttpMapper覆盖409/422/429/503；未知码500且脱敏 | 保留ServiceResult错误协议，无需另造ProblemDetails；补OpenAPI声明而非第二套runtime响应 |
| API-14 | Can Keep，成功shape例外 | auth/login返回LoginTokenDto，auth/me返回MeDto，其他多数ServiceResult，download二进制，HEAD无正文 | transport按契约解析，不统一把所有成功响应当data字段 |
| API-15 | Must Fix，自动生成前 | runtime StringEnumConverter，Swagger无Newtonsoft支持接线；DTO可能被导出为数字枚举而线上输出名称 | 在原Swashbuckle上接Newtonsoft schema支持并对照样本；保留实体数据库整数枚举与HTTP字符串枚举的职责区别 |
| API-16 | Must Fix，自动生成前 | Contracts netstandard2.0未开启NRT；参考类型可null，runtime保留null；nullable数值显式?，成功Error=null、失败Data=null | 明确nullable/required语义，schema补充不破坏netstandard2.0；测试null、缺省、必填集合，TS开启strict |
| API-17 | Should Fix，取消 | 273条声明中110条签名无CancellationToken（含继承和HTTP计数）；现代单据多有token，手工来源及旧资料CRUD缺；有token不代表仓储全链传递 | 按新触碰查询传递RequestAborted，避免浏览器取消仍耗资源；已提交写入取消不能视作回滚 |
| API-18 | Must Fix，跨日期/时区用例前 | BaseDto用DateTime?，BaseService有DateTime.Now，PG参数显式区分timestamp与timestamptz；无全局业务日期语义约定 | 列字段矩阵：业务日期/本地墙上时间/UTC时刻；未分清不能统一toISOString或给旧值补Z |
| API-19 | Can Keep，Guid | Guid路径/DTO与PG uuid可映射TS string；通用仓储新增会覆盖Uid生成新值 | 浏览器crypto.randomUUID用于ClientKey/临时技术身份；必须以响应Uid重映射，不能要求复制旧顺序Guid算法 |
| API-20 | Must Fix，对应5用例上线前 | ProcurementDemandAssociationController的context/candidates/preview/associate与ForExcelPurchaseOrderController.CheckAsync均未登记SingleKeys或其他分支 | 决定已有细键后登记并验证无权拒绝/有权到达；不去掉默认拒绝，不通过Admin绕过 |
| API-21 | Can Keep，文件政策 | FileAccessPolicy活动用户可读、Admin可写；端点还要求System.Files.Manage；PermissionCatalog有保留权限 | 明确UI按真实双层条件，不推断普通用户获键即可上传；非管理员上传需单独业务授权 |
| API-22 | Should Fix，旧Refit契约 | IGenericApi的TDto/Guid响应与BaseController不一致；IProductApi.GetCategoryTreeAsync没有实际路径 | 核查实际调用方后修兼容；TS从OpenAPI生成，禁止以Refit作为唯一协议事实 |

具体代码：
[BaseService](../SwiftExport.Application/Services/Base/BaseService.cs)、[Product单项重写](../SwiftExport.Application/Services/Products/Catalog/Maintenance/ProductMaintenanceService.SaveBoundary.cs)、[SupplierService](../SwiftExport.Application/Services/Suppliers/Management/SupplierService.cs)、[CategoryService](../SwiftExport.Application/Services/Products/Categories/ProductCategoryService.cs)、[SaveItemResultFactory](../SwiftExport.Contracts/Common/SaveItemResultFactory.cs)、[ServiceResultHttpFilter](../SwiftExport.WebApi/ErrorHandling/ServiceResultHttpFilter.cs)、[ServiceResult](../SwiftExport.Contracts/Wrappers/ServiceResult.cs)、[时间参数](../SwiftExport.Infrastructure/Repositories/Base/PostgreSqlParameters.cs)、[通用仓储](../SwiftExport.Infrastructure/Repositories/Base/PostgreSqlDapperRepository.cs)、[FileAccessPolicy](../SwiftExport.Application/Services/Files/FileAccessPolicy.cs)。

以上Must Fix均为后续授权任务；本轮不修改。

## 3. OpenAPI / Swagger专项

宿主引用Swashbuckle.AspNetCore **10.2.3**，注册ApiExplorer与SwaggerGen，只有CustomSchemaIds，Development才公开Swagger/UI。未发现ProducesResponseType、Consumes、security definition/requirement、Newtonsoft schema支持或文件专用operation filter。Swagger生成器存在不等于完整schema，实际OpenAPI版本和完整JSON尚待运行确认。

| 检查点 | 静态结论 | 首次生成前验收 |
| --- | --- | --- |
| Endpoint覆盖 | 显式Http属性与继承CRUD可被探索；实际273条静态声明与paths合并计数会不同 | 用MVC action descriptor对照，验证5个被拒绝路径不能当可用 |
| DTO schema | 强类型声明可提供模型；Auth所有action用IActionResult，无法单靠返回声明得出成功模型 | login明确LoginTokenDto，me明确MeDto，其余明确ServiceResult具体T |
| Enum | Newtonsoft StringEnumConverter已配置；Swashbuckle默认serializer模型不自动等同Newtonsoft | 选择真实ConfirmationStatusEnum样本，对比schema.enum与实际JSON字符串 |
| Nullable/required | 数值Guid?等有信息；旧参考类型、computed getter、null的Data/Error需校准 | 成功/失败/空列表/null字段样本、严格TS编译，不以?等同可省略 |
| 泛型、分页、BatchSave | 闭合泛型原则上可导出，不需要TS支持CLR泛型本身 | 核验ServiceResult<PagedResponseBase<ProductDto>>、ProductBatchSaveRequest/Response和SaveItemResult |
| schema ID | CustomSchemaIds直接用Type.FullName.Replace('+','.')；闭合泛型FullName含反引号、方括号/逗号、程序集信息 | 这是静态风险，尚未看到实际失败JSON；校验所有component key与$ref，采用稳定递归类型名，保留同名DTO命名空间区分 |
| operationId/tag | 没有显式稳定operationId策略 | 检查唯一性/稳定性，按Controller+方法区分同名Search；既有路径无需重命名 |
| 统一错误 | 实际ServiceResult<object>.Fail经Filter/中间件输出；不是ProblemDetails | 描述400/401/403/404/409/422/429/500/503与error字段，413/415按上传；定义default错误响应 |
| JWT | runtime Bearer已配置；文档没有Bearersecurity schema | schema定义http bearer；匿名login/client-version不要求token，其他endpoint按实际身份标注 |
| 上传原始流 | PUT files/{containerId}只在内部读Request.Body，没有显式body参数 | requestBody application/octet-stream、binary；path/overwrite为query |
| multipart | ProductImageController手动MultipartReader只接收重复files节，无IFormFile参数 | requestBody multipart/form-data对象files=array(binary)，写清逐项响应；不能只加Consumes就认为body已导出 |
| 下载/HEAD | IActionResult返回File流，enableRangeProcessing=true；无成功内容声明 | GET 200/206 binary、HEAD无body；描述Range/Content-Range、Content-Disposition及416 |
| 生产Swagger | Development分支发布，生产不公开 | CI离线导出schema或隔离Development宿主；不为生成器强开生产UI |

据[Swashbuckle官方说明](https://github.com/domaindrivendev/Swashbuckle.AspNetCore/blob/master/README.md)，Newtonsoft需要显式支持接线。组件名称应遵循[OpenAPI组件规范](https://spec.openapis.org/oas/v3.0.3.html#components-object)。这是针对现有配置的风险判断；没有实测schema时不声称“全部泛型无法生成”或“已有生成失败”。

**当前不能依赖自动生成完整正确调用的接口族**：Auth成功类型未声明、文件原始流/手动multipart/下载缺元数据、模糊单项DELETE，以及所有未声明的运行错误。强类型page/batch-save具备基础，仍需枚举、nullable与schema ID验证。

推荐顺序：保留Swashbuckle → 补serializer/成功与错误/二进制声明 → 导出全量schema → lint与$ref校验 → 为Product、auth、files做生成编译样例 → 真实HTTP样本对照 → CI固定输出。不要先切NSwag服务器生成器来掩盖原契约缺口。

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
| P0 | 首批OpenAPI完整性 | auth/me/login、Product page/batch、字典/分类读取以及所需错误完整；全schema能lint、生成、tsc；无需先补所有未来页面 |
| P0 | 浏览器网络基线 | 推荐生产同origin反向代理/api和图片，开发Vite proxy；若直连不同origin则明确AddCors/UseCors、OPTIONS与允许origin/header/method；现在未实现CORS |
| P0 | JWT使用 | 现有Bearer可用；明确过期401保留草稿、重新登录，首次me加载及权限；DevUser关闭场景验证；不要求先上Refresh Token |
| P0 | 错误与并发 | 首批Product stale Version稳定409；其他模块接入前补对应码；两层success、OUTCOME_UNKNOWN、429有明确体验，无盲重试 |
| P0 | DTO数据含义 | Guid临时到持久Uid映射、null/optional、enum、业务日期/时刻；金钱输入保留十进制文本并在boundary转现有decimal JSON，金额/舍入仍服务端权威 |
| P0（对应模块） | 5端点权限登记 | 采购需求关联/采购导入上线前登记；Product PoC无需等待这些功能 |
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

未验证运行Swagger、TS生成、浏览器跨域、真实PG并发；未扫描或公布配置密钥值。部署是否已有反向代理CORS/缓存、数据库是否保留历史授权表、静态MainImage是否均为HTTP可用地址，本轮无法确认。

未来最小验证可复用[HttpErrorContractTests](../SwiftExport.Application.Tests/Authentication/HttpErrorContractTests.cs)、[JwtAndControllerContractTests](../SwiftExport.Application.Tests/Authentication/JwtAndControllerContractTests.cs)、[EndpointPermissionHttpTests](../SwiftExport.Application.Tests/Authentication/EndpointPermissionHttpTests.cs)、[FileApiIntegrationTests](../SwiftExport.Application.Tests/Files/FileApiIntegrationTests.cs)。真实PG写测试须另有明确实例/库/数据与清理授权，文档规划不构成数据库写入授权。

