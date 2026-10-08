# Web 客户端开发前可用性盘点（初次静态快照）

审查日期：2026-10-07。范围：当前工作树的 WebApi、Contracts、相关 Application/Infrastructure 调用链与 WPF 消费者。主角色 Planner，采用[业务规划工作流](../.agents/workflows/业务规划工作流.md)。本轮只新增六份文档，不修改业务源码、运行配置、测试、数据库或既有治理入口。

**2026-10-08 状态说明**：以下正文及附录保留 2026-10-07 初次审计时的实现快照，71/273 不是当前重新扫描计数。随后路由/body 身份、包装 Version 和五端点权限登记已修复，首批九端点已完成 OpenAPI/隔离 HTTP/Orval 验收；旧正文的“未登记/缺声明/未生成”不能直接当作当前待修任务。当前状态查 [差距跟踪](web-api-gap-analysis.md) 与 [首批验收](Web首批接口契约验收.md)；Web 方案查 [总决策](Web客户端架构与脚手架决策.md) 与 [多标签工作区](Web多标签工作区设计.md)。本次仅加此说明，不改写历史端点清单或宣称全量重验收。

## 1. 结论与证据边界

后端已具备启动 Web PoC 的业务基础，但尚不满足“可靠地从 OpenAPI 自动生成客户端并直接推广所有模块”的完成条件。查询、现有业务命令、数据库事务、RBAC、分页包装均应复用；需先补契约描述、确认浏览器接入方式和保存失败协议。

静态盘点得到 **71 个具体 Controller、273 条 HTTP 方法/路由声明**，其中包括继承 CRUD，GET 与 HEAD 分开计数；不是 273 个独立业务用例，也不是运行成功率。5 条声明未登记全局权限，见差距报告。源码中约 268/273 条声明已存在权限登记或显式身份/服务内分支，这是接线覆盖率，不能当作“98% 已通过 Web 验收”。业务复用量不宜虚报精确百分比：核心业务层可继续使用，Web 的交互、认证存储、类型生成和编辑状态仍需新建。

本轮未启动宿主、获取运行 OpenAPI、生成 TS Client、访问数据库、执行 .NET 构建或业务测试。接口绑定、serializer 输出、schema、部署配置和并发行为中需要运行证据的部分均标“待确认”；现有测试文件仅是可复用验证入口。

背景校正：当前为 **.NET 10 + PostgreSQL/Npgsql + Dapper**，不是 SQL Server。依据[Infrastructure DI](../SwiftExport.Infrastructure/DependencyInjection.cs)、[当前 PG 基线](../.agents/references/PostgreSQL当前基线.md)。项目实际宿主名是 SwiftExport.WebApi；需求中的 SwiftExport.Api 解释为 WPF/Web 共用此同一服务，未提出第二套 API。

现有[BS React 方案](BS前端React技术方案与后续计划.md)曾推荐 AG Grid 和 `/api/Product/search`；当前 ProductController 实际为 `POST /api/Product/page`。本轮按用户指定采用 Ant Design Table + SwiftDataGrid，保留旧方案作为差异证据，不恢复正在被删除的 demos，不修改旧文档。

## 2. 读取索引

| 主题 | 当前源码依据 |
| --- | --- |
| 宿主、JSON、Swagger、认证、错误管道 | [WebApiConfiguration](../SwiftExport.WebApi/Configuration/WebApiConfiguration.cs)、[Program](../SwiftExport.WebApi/Program.cs)、[项目依赖](../SwiftExport.WebApi/SwiftExport.WebApi.csproj) |
| 分页与批量保存 | [PagedRequestBase](../SwiftExport.Contracts/Wrappers/PagedRequestBase.cs)、[PagedResponseBase](../SwiftExport.Contracts/Wrappers/PagedResponseBase.cs)、[BatchSaveRequestBase](../SwiftExport.Contracts/Wrappers/BatchSaveRequestBase.cs)、[BatchSaveResponseBase/SaveItemResult](../SwiftExport.Contracts/Wrappers/BatchSaveResponseBase.cs) |
| 权限权威 | [EndpointPermissionFilter](../SwiftExport.WebApi/Authentication/EndpointPermissionFilter.cs)、[PermissionKeys](../SwiftExport.Contracts/DTOs/Auth/Permissions/PermissionKeys.cs)、[PermissionEvaluationService](../SwiftExport.Application/Services/Auth/Permissions/Evaluation/PermissionEvaluationService.cs) |
| 事务与重放 | [CommandPipeline](../SwiftExport.Application/Commands/CommandPipeline.cs)、[BusinessCommandExecutor](../SwiftExport.Application/Commands/BusinessCommandExecutor.cs) |
| 真实模块边界 | [能力地图索引](../.agents/references/业务模块能力与页面契约治理规则.md)、[业务架构](../.agents/references/业务架构总纲.md) |
| WPF 权限与编辑体验 | [CurrentUserService](../SwiftExport.Client.Wpf/Authentication/CurrentUserService.cs)、[NavigationAccessService](../SwiftExport.Client.Wpf/Navigation/NavigationAccessService.cs)、[BaseListModel](../SwiftExport.Client.Wpf/Mvvm/BaseListModel.cs)、[BaseLineModel](../SwiftExport.Client.Wpf/Mvvm/BaseLineModel.cs) |

附录逐 Controller 列出具体 Endpoint、请求绑定/DTO、返回类型和权限 Key，并链接真实文件；Contracts 类型索引在每个 Controller 后。以下能力表与附录联合阅读，不以表名推断实现。

## 3. 模块能力矩阵

符号：是=存在对应声明；专用=通过用例命令，不是通用 CRUD；无=本次未发现公开入口；“固定排序”指仓储排序，不是接受客户端排序参数。

| 模块 | 分页/搜索 | 排序/筛选 | 详情 | C/U/D | BatchSave | Version | React 适用性 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Authentication | admin/users 为摘要，登录不分页 | 不适用 | me 已有 | register、改密、重置；无删用户 | 无 | CredentialVersion；不是行 Version | 可复用；login/me 裸 DTO，schema 缺声明 |
| User | POST system/users/page；Keyword | 固定；ManagedPageRequest | 是 | 注册在 auth；更新/启停；无 DELETE | 无 | ExpectedVersion | 可复用；写入仅有效内置管理员 |
| Role | POST system/roles/page | 固定；专用条件 | 是 | 是/是/是 | 无；关系整组替换 | ExpectedVersion | 可复用；非任意角色可管理 |
| Permission | GET system/permissions 全目录 | 无任意排序/动态表达式 | 无单项详情 | 无公开 CRUD；显式同步代码目录 | 无 | 授权关系用聚合版本 | 可复用；me 已含当前用户权限 |
| Product | page、lookup、sku、customerCodes | 固定 SKU+Uid；强类型多条件 | 是 | 是/是/是（单项也走批量内核） | 是，整批事务 | 包装 Version | PoC 首选；all 明确返回失败；避免模糊单项路径绑定 |
| ProductCategory | page Keyword、all | 固定 SortNo/名称/Uid；无动态表达式 | 是 | 继承 CRUD | 是，父子拓扑处理 | 是 | 树由 flat ParentId 构建；暂不假设 tree endpoint |
| ProductMaster | page、assign-category、detail | 固定；分类集合/未分类 | 是 | 批量新增/改/删；save-detail | 是及详情专用保存 | 是 | 可复用；不要重复造主型号/分类字段 |
| ProductNeutral/Manager | manager/page | 固定；分类/颜色 | 归属主型号详情 | 主型号内批量、manager批量 | 是；返回形状不同 | 是 | 同一产品资料族；adapter区分详情快照与SaveItemResult |
| ProductColor/BOM | Color page/all；BOM by-no/tree/validate | 固定；BOM没有公开page，不能由BomSearchRequest推断 | 是 | 是 | 是 | 是 | 可复用；BomVersion是有效性，不做历史版本选择 |
| Supplier | page/all、Keyword/BatchKeywords | 固定 SupplierCode+Uid | 是 | 是 | 是，事务 | 是；单项BaseService需核对 | 可复用；并发失败分类待改善 |
| SupplierQuote（实际 SupplierProduct） | page，SupplierId+Keyword | 固定 SupplierSku+Uid | 是 | 是 | 是 | 是 | 报价为 UnitPrice/TaxPrice 资料；没有独立 SupplierQuote 生命周期 API |
| Customer/CustomerProduct/PoConfig | page，客户SKU、双方配置 | 固定；对应 DTO 强类型条件 | 是 | 是 | 是 | 是 | 可复用；不与产品库存身份混合 |
| SalesOrder | manager/search；候选分页；fulfillment/{uid} | 固定；客户、订单、日期、状态、QuickView | 是 | save 支持建/改；delete-draft | 无通用BatchSave；专用头行Save | 头/行版本+CommandId | 可复用命令；无商业修订、恢复、行终止公开入口 |
| AfterSales | GET列表/lookup 分页 | 固定；客户、责任供应商、状态、日期 | 是 | create/save；无公开DELETE，void为生命周期 | 专用Added/Updated/DeletedLines | Version+CommandId | 正数量结案仍阻断，不能展示为已完成能力 |
| 售后品质分类 | all平面树 | 树排序由资料字段；无分页 | 无独立 | batch维护 | 是 | 是 | 查看/维护使用售后权限，非三组派生CRUD键 |
| 来源/物料需求 | search、material-search、分组、candidate | 固定；来源、SKU、日期、供应商等 | 详情/Trace | 手工来源建/改/关；建立/回退/供应商命令 | 无普通CRUD批量 | 版本/指纹/CommandId按专用DTO | 可复用；不是完整MRP，不能前端重算执行数量 |
| PurchaseOrder | orders/search、order-manager/search、进度search | 固定；供应商、客户、日期、状态 | 详情/行/Trace | 独立创建、commercial保存、delete-lines/void | purchase-drafts/create 为领域批量命令 | ExpectedVersion/CommandId | 主链可复用；需求关联4端点被全局权限拒绝 |
| Inbound | search、sources/search | 固定；类型/过账状态 | 是 | 专用草稿建/改/delete | 无普通BatchSave | ExpectedVersion/CommandId | Preview/Post/Reverse独立，不能由Grid直接写库存 |
| Outbound | search、items/search | 固定；类型/过账状态 | 是 | 专用草稿建/改/delete | 无普通BatchSave | ExpectedVersion/CommandId | 人工StockIssue与箱单自动单据分开 |
| Inventory/Warehouse | balances/search、ledger/search、warehouse picker/search | 固定；仓、供应商、流水类型/日期 | Trace/仓详情 | 余额流水只读；仓库建/改/启停/删 | 无 | 仓库命令版本；流水不可变 | 可复用；不是自由CRUD库存系统 |
| PackingList | search、sources/search | 固定；客户、日期、过账状态 | 详情/Trace | 草稿建/改/delete、来源关联 | 专用整单命令 | ExpectedVersion/CommandId/PreviewToken | 可复用；整单过账与反向，不实现前端补库存 |
| File/Attachment | 无列表/分页搜索 | 容器+path定位 | 二进制GET/HEAD | PUT流上传，可选overwrite；无删接口 | 产品图片multipart batch允许逐项成功 | 无通用行Version | 需要显式schema与浏览器Blob；不是业务附件元数据模块 |
| Dictionary/Currency/Exchange | page/all/by-code/group-tree/base/enabled | 固定；Keyword、部分BatchKeywords | 是 | 继承CRUD；本位币切换专用 | 是 | 是 | 可复用；本位币切换重设RateToBase需展示Message |
| ExcelReadProfile/Numbering | profiles按groupCode列表；next命令 | 固定；无统一分页 | profile详情 | profileCRUD；next发号非只读 | 无 | profile expectedVersion | 可复用但不作为Web业务页面的核心依赖 |
| ForExcel/ClientVersion | 检查JSON导入数据；版本策略GET | 不适用 | 不适用 | 检查不是正式保存 | 无 | 导入Issue/CanSave | Excel DTO可浏览器消费；采购check未登记；版本策略偏桌面客户端 |

**动态筛选结论**：公开搜索请求提供多条件/集合/日期范围，不包含统一字段-运算符表达式 DSL；本轮没有找到通用 SortBy/SortDirection 请求契约。不能把 Ant Table 的列排序/筛选回调直接当成后端支持任意排序/筛选。Product 的实际筛选 SQL见[ProductRepository.Paging](../SwiftExport.Infrastructure/Repositories/Products/Catalog/PostgreSqlProductRepository.Paging.cs)，报价见[SupplierProductRepository](../SwiftExport.Infrastructure/Repositories/Suppliers/SupplierProducts/PostgreSqlSupplierProductRepository.cs)。已有固定排序可保留；有真实全局排序需求再增加白名单字段。

## 4. 浏览器与 WPF/Refit 耦合判断

业务 DTO 大多是 Guid、标量、列表、枚举及普通包装，没有要求浏览器运行 WPF 的 HTTP 设计。Contracts项目含[UiInterface/IDialogService](../SwiftExport.Contracts/UiInterface/IDialogService.cs)，但不因此推断所有 API schema 混入弹窗依赖；只导出 HTTP 可达契约。

Refit 并非服务器协议。现有[IGenericApi](../SwiftExport.Contracts/Apis/BaseApi/IGenericApi.cs)仍以裸TDto入参和ServiceResult<Guid>表示新增/更新/删除，与BaseController的请求包装和SaveItemResult不一致；[IProductApi](../SwiftExport.Contracts/Apis/ProductApis/Catalog/IProductApi.cs)还声明 `/product-categories/tree`，当前Controller没有对应路径。Web生成应以Controller+OpenAPI为事实，不从Refit方法复制。已被WPF使用的page/batch-save可继续复用，不能因旧泛型契约而重写稳定业务API。

HTTP Method 总体可自然消费：POST复杂查询、POST业务动作和整批保存都可保留。GET售后查询是另一种绑定方式，同样可用。主要问题是单项DELETE复杂参数、PUT路由ID未校验及schema不完整，详见[差距报告](web-api-gap-analysis.md)。

二进制下载不适合统一JSON解析；成功login/me不适合强制ServiceResult解包；批次成功不等于每行成功。前端 transport必须按端点的声明处理，详见[前端架构](web-frontend-architecture.md)。

## 5. 验证与待确认

- 已完成：真实Controller静态全表、继承CRUD补齐、Contracts类型与权限矩阵对照、代表性仓储/保存服务/WPF消费者核查。
- 未完成：运行Swagger、严格OpenAPI验证、TS生成编译、跨域预检、真实JWT/权限调用、PG写入/并发、浏览器文件行为。不存在本轮“全模块测试通过”结论。
- 现有验证入口：[HttpErrorContractTests](../SwiftExport.Application.Tests/Authentication/HttpErrorContractTests.cs)、[EndpointPermissionFilterTests](../SwiftExport.Application.Tests/Authentication/EndpointPermissionFilterTests.cs)、[RbacPermissionEvaluationTests](../SwiftExport.Application.Tests/Authentication/RbacPermissionEvaluationTests.cs)、[FileApiIntegrationTests](../SwiftExport.Application.Tests/Files/FileApiIntegrationTests.cs)、[ProductImageApiIntegrationTests](../SwiftExport.Application.Tests/Files/ProductImageApiIntegrationTests.cs)；本轮未运行。
- 尚须确认：部署origin/反向代理、实际JWT配置、图片公共目录映射、现有存量MainImage值、PoC测试账号/库范围、业务日期含义、金额精度/舍入、注册用户角色安排。不得将这些未决条件写成已部署能力。
- 初始工作树存在用户改动：VSTO、GrossDemandDecompositionPlanner及demo删除等。审查采用当前工作树快照，其他任务继续变化可能使行号或结论过期；本轮不接管这些文件。

## 6. 全量声明附录

以下是静态声明目录，保留源码路由大小写；LowercaseUrls配置不等于重写全部Controller模板。接口均需身份，例外login和client-version明确匿名；文件静态图片不属于Controller目录。批量权限 `{Create/Edit/Delete}` 表示按实际非空操作集合分别检查。Rbac查询/写入分别在服务内检查，不因全局Filter跳过就视为无权限。

请求列保留C#参数，FromServices与CancellationToken为宿主依赖、取消机制，**不应生成浏览器参数**。Task/ActionResult是声明容器，线上JSON取其内部类型；继承CRUD见BaseController。签名不能替代ApiExplorer实际绑定验证。

### AfterSalesConfirmationController

源码：[AfterSalesConfirmationController](../SwiftExport.WebApi/Controllers/QualityControl/AfterSales/Confirmation/AfterSalesConfirmationController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| POST `/api/quality-control/after-sales/{uid:guid}/confirm-demand` | `Guid uid, [FromBody] ConfirmAfterSalesDemandCommand command, CancellationToken token` | `Task<ServiceResult<AfterSalesCommandResult>>` | `QualityControl.AfterSales.Confirm` |
| POST `/api/quality-control/after-sales/{uid:guid}/cancel-demand-confirmation` | `Guid uid, [FromBody] CancelAfterSalesDemandConfirmationCommand command, CancellationToken token` | `Task<ServiceResult<AfterSalesCommandResult>>` | `QualityControl.AfterSales.CancelConfirmation` |

契约源码：[ConfirmAfterSalesDemandCommand](../SwiftExport.Contracts/DTOs/QualityControl/AfterSales/Confirmation/ConfirmAfterSalesDemandCommand.cs)、[CancelAfterSalesDemandConfirmationCommand](../SwiftExport.Contracts/DTOs/QualityControl/AfterSales/Confirmation/CancelAfterSalesDemandConfirmationCommand.cs)、[AfterSalesCommandResult](../SwiftExport.Contracts/DTOs/QualityControl/AfterSales/Results/AfterSalesCommandResult.cs)。

### AfterSalesLifecycleController

源码：[AfterSalesLifecycleController](../SwiftExport.WebApi/Controllers/QualityControl/AfterSales/Lifecycle/AfterSalesLifecycleController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| POST `/api/quality-control/after-sales/{uid:guid}/close` | `Guid uid, [FromBody] CloseAfterSalesCommand command, CancellationToken token` | `Task<ServiceResult<AfterSalesCommandResult>>` | `QualityControl.AfterSales.Lifecycle` |
| POST `/api/quality-control/after-sales/{uid:guid}/reopen` | `Guid uid, [FromBody] ReopenAfterSalesCommand command, CancellationToken token` | `Task<ServiceResult<AfterSalesCommandResult>>` | `QualityControl.AfterSales.Lifecycle` |
| POST `/api/quality-control/after-sales/{uid:guid}/void` | `Guid uid, [FromBody] VoidAfterSalesCommand command, CancellationToken token` | `Task<ServiceResult<AfterSalesCommandResult>>` | `QualityControl.AfterSales.Lifecycle` |

契约源码：[AfterSalesCommandResult](../SwiftExport.Contracts/DTOs/QualityControl/AfterSales/Results/AfterSalesCommandResult.cs)、[VoidAfterSalesCommand](../SwiftExport.Contracts/DTOs/QualityControl/AfterSales/Lifecycle/VoidAfterSalesCommand.cs)、[ReopenAfterSalesCommand](../SwiftExport.Contracts/DTOs/QualityControl/AfterSales/Lifecycle/ReopenAfterSalesCommand.cs)、[CloseAfterSalesCommand](../SwiftExport.Contracts/DTOs/QualityControl/AfterSales/Lifecycle/CloseAfterSalesCommand.cs)。

### AfterSalesMaintenanceController

源码：[AfterSalesMaintenanceController](../SwiftExport.WebApi/Controllers/QualityControl/AfterSales/Maintenance/AfterSalesMaintenanceController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| POST `/api/quality-control/after-sales` | `[FromBody] SaveAfterSalesCommand command, CancellationToken token` | `Task<ServiceResult<AfterSalesCommandResult>>` | `QualityControl.AfterSales.Maintain` |
| PUT `/api/quality-control/after-sales/{uid:guid}` | `Guid uid, [FromBody] SaveAfterSalesCommand command, CancellationToken token` | `Task<ServiceResult<AfterSalesCommandResult>>` | `QualityControl.AfterSales.Maintain` |

契约源码：[AfterSalesCommandResult](../SwiftExport.Contracts/DTOs/QualityControl/AfterSales/Results/AfterSalesCommandResult.cs)、[SaveAfterSalesCommand](../SwiftExport.Contracts/DTOs/QualityControl/AfterSales/Maintenance/SaveAfterSalesCommand.cs)。

### AfterSalesQualityCategoryController

源码：[AfterSalesQualityCategoryController](../SwiftExport.WebApi/Controllers/QualityControl/Categories/AfterSalesQualityCategoryController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| GET `/api/quality-control/after-sales/quality-categories/all` | `CancellationToken cancellationToken` | `Task<ServiceResult<List<AfterSalesQualityCategoryDto>>>` | `QualityControl.AfterSales.View` |
| POST `/api/quality-control/after-sales/quality-categories/batch-save` | ` [FromBody] AfterSalesQualityCategoryBatchSaveRequest request, CancellationToken cancellationToken` | `Task<ServiceResult<AfterSalesQualityCategoryBatchSaveResponse>>` | `QualityControl.AfterSales.Maintain` |

契约源码：[AfterSalesQualityCategoryDto](../SwiftExport.Contracts/DTOs/QualityControl/Categories/AfterSalesQualityCategoryDto.cs)、[AfterSalesQualityCategoryBatchSaveResponse](../SwiftExport.Contracts/DTOs/QualityControl/Categories/AfterSalesQualityCategoryBatchSaveResponse.cs)、[AfterSalesQualityCategoryBatchSaveRequest](../SwiftExport.Contracts/DTOs/QualityControl/Categories/AfterSalesQualityCategoryBatchSaveRequest.cs)。

### AfterSalesQueryController

源码：[AfterSalesQueryController](../SwiftExport.WebApi/Controllers/QualityControl/AfterSales/Query/AfterSalesQueryController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| GET `/api/quality-control/after-sales` | `[FromQuery] AfterSalesSearchRequest request, CancellationToken token` | `Task<ServiceResult<PagedResponseBase<AfterSalesListItemDto>>>` | `QualityControl.AfterSales.View` |
| GET `/api/quality-control/after-sales/{uid:guid}` | `Guid uid, CancellationToken token` | `Task<ServiceResult<AfterSalesDetailDto>>` | `QualityControl.AfterSales.View` |
| GET `/api/quality-control/after-sales/lookups/{kind}` | `string kind, [FromQuery] AfterSalesLookupRequest request, CancellationToken token` | `Task<ServiceResult<PagedResponseBase<AfterSalesLookupDto>>>` | `QualityControl.AfterSales.View` |

契约源码：[AfterSalesSearchRequest](../SwiftExport.Contracts/DTOs/QualityControl/AfterSales/Query/AfterSalesSearchRequest.cs)、[AfterSalesLookupRequest](../SwiftExport.Contracts/DTOs/QualityControl/AfterSales/Query/AfterSalesLookupRequest.cs)、[AfterSalesLookupDto](../SwiftExport.Contracts/DTOs/QualityControl/AfterSales/Query/AfterSalesLookupDto.cs)、[AfterSalesListItemDto](../SwiftExport.Contracts/DTOs/QualityControl/AfterSales/Query/AfterSalesListItemDto.cs)、[AfterSalesDetailDto](../SwiftExport.Contracts/DTOs/QualityControl/AfterSales/Query/AfterSalesDetailDto.cs)。

### AuthController

源码：[AuthController](../SwiftExport.WebApi/Controllers/Auth/AuthController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| POST `/api/auth/login` | `[FromBody] LoginRequestDto request, CancellationToken cancellationToken` | `Task<IActionResult>` | `身份专用，见模块说明` |
| GET `/api/auth/me` | `CancellationToken cancellationToken` | `Task<IActionResult>` | `身份专用，见模块说明` |
| POST `/api/auth/register` | `[FromBody] RegisterRequestDto request, CancellationToken cancellationToken` | `Task<IActionResult>` | `System.Users.Manage` |
| POST `/api/auth/change-password` | `[FromBody] ChangePasswordRequestDto request, CancellationToken cancellationToken` | `Task<IActionResult>` | `身份专用，见模块说明` |
| POST `/api/auth/admin/reset-password` | `[FromBody] AdminResetPasswordRequestDto request, CancellationToken cancellationToken` | `Task<IActionResult>` | `System.Users.Manage` |
| GET `/api/auth/admin/users` | `CancellationToken cancellationToken` | `Task<IActionResult>` | `System.Users.View` |

契约源码：[RegisterRequestDto](../SwiftExport.Contracts/DTOs/Auth/Users/RegisterRequestDto.cs)、[ChangePasswordRequestDto](../SwiftExport.Contracts/DTOs/Auth/Users/ChangePasswordRequestDto.cs)、[AdminResetPasswordRequestDto](../SwiftExport.Contracts/DTOs/Auth/Users/AdminResetPasswordRequestDto.cs)、[LoginRequestDto](../SwiftExport.Contracts/DTOs/Auth/Session/LoginRequestDto.cs)。

### BomController

源码：[BomController](../SwiftExport.WebApi/Controllers/Products/BomController.cs)；继承[BaseController](../SwiftExport.WebApi/Controllers/Base/BaseController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| POST `/api/Bom/batch-save` | `[FromBody] BomBatchSaveRequest req` | `Task<ServiceResult<BomBatchSaveResponse>>` | `Products.Bom.{Create/Edit/Delete}` |
| GET `/api/Bom/tree/{productSku}` | `string productSku` | `Task<ServiceResult<IEnumerable<BomTreeDto>>>` | `Products.Bom.View` |
| GET `/api/Bom/by-no/{bomNo}` | `string bomNo` | `Task<ServiceResult<List<BomDto>>>` | `Products.Bom.View` |
| GET `/api/Bom/validate/{productSku}` | `string productSku` | `Task<ServiceResult<bool>>` | `Products.Bom.View` |
| GET `/api/Bom/{id}` | `Guid id` | `Task<ServiceResult<BomDto>>` | `Products.Bom.View` |
| POST `/api/Bom` | `[FromBody] CreateItemRequest<BomDto> dto` | `Task<ServiceResult<SaveItemResult>>` | `Products.Bom.Create` |
| PUT `/api/Bom/{id}` | `Guid id, [FromBody] UpdateItemRequest<BomDto> dto` | `Task<ServiceResult<SaveItemResult>>` | `Products.Bom.Edit` |
| DELETE `/api/Bom/{id}` | `DeleteItemRequest id` | `Task<ServiceResult<SaveItemResult>>` | `Products.Bom.Delete` |

契约源码：[BomTreeDto](../SwiftExport.Contracts/DTOs/Products/BomTreeDto.cs)、[BomDto](../SwiftExport.Contracts/DTOs/Products/BomDto.cs)、[BomBatchSaveResponse](../SwiftExport.Contracts/DTOs/Products/Responses/BomBatchSaveResponse.cs)、[BomBatchSaveRequest](../SwiftExport.Contracts/DTOs/Products/Requests/BomBatchSaveRequest.cs)。

### ClientVersionController

源码：[ClientVersionController](../SwiftExport.WebApi/Controllers/Systems/ClientVersions/ClientVersionController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| GET `/api/client-version` | `无` | `ServiceResult<ClientVersionInfo>` | `身份专用，见模块说明` |

契约源码：[ClientVersionInfo](../SwiftExport.Contracts/DTOs/Updates/ClientVersionInfo.cs)。

### CustomerController

源码：[CustomerController](../SwiftExport.WebApi/Controllers/Customers/Management/CustomerController.cs)；继承[BaseController](../SwiftExport.WebApi/Controllers/Base/BaseController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| POST `/api/Customer/page` | `[FromBody] CustomerSearchRequest request` | `Task<ServiceResult<PagedResponseBase<CustomerDto>>>` | `Customers.Customer.View` |
| POST `/api/Customer/batch-save` | `[FromBody] CustomerBatchSaveRequest req` | `Task<ServiceResult<CustomerBatchSaveResponse>>` | `Customers.Customer.{Create/Edit/Delete}` |
| GET `/api/Customer/{id}` | `Guid id` | `Task<ServiceResult<CustomerDto>>` | `Customers.Customer.View` |
| POST `/api/Customer` | `[FromBody] CreateItemRequest<CustomerDto> dto` | `Task<ServiceResult<SaveItemResult>>` | `Customers.Customer.Create` |
| PUT `/api/Customer/{id}` | `Guid id, [FromBody] UpdateItemRequest<CustomerDto> dto` | `Task<ServiceResult<SaveItemResult>>` | `Customers.Customer.Edit` |
| DELETE `/api/Customer/{id}` | `DeleteItemRequest id` | `Task<ServiceResult<SaveItemResult>>` | `Customers.Customer.Delete` |

契约源码：[CustomerDto](../SwiftExport.Contracts/DTOs/Customers/Management/CustomerDto.cs)、[CustomerBatchSaveResponse](../SwiftExport.Contracts/DTOs/Customers/Management/Responses/CustomerBatchSaveResponse.cs)、[CustomerSearchRequest](../SwiftExport.Contracts/DTOs/Customers/Management/Requests/CustomerSearchRequest.cs)、[CustomerBatchSaveRequest](../SwiftExport.Contracts/DTOs/Customers/Management/Requests/CustomerBatchSaveRequest.cs)。

### CustomerPoConfigController

源码：[CustomerPoConfigController](../SwiftExport.WebApi/Controllers/Customers/PoConfigs/CustomerPoConfigController.cs)；继承[BaseController](../SwiftExport.WebApi/Controllers/Base/BaseController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| GET `/api/CustomerPoConfig/by-parties` | ` [FromQuery] Guid customerId, [FromQuery] Guid supplierId, CancellationToken cancellationToken` | `Task<ServiceResult<CustomerPoConfigDto>>` | `Customers.PoConfig.View` |
| POST `/api/CustomerPoConfig/page` | `[FromBody] CustomerPoConfigSearchRequest request` | `Task<ServiceResult<PagedResponseBase<CustomerPoConfigDto>>>` | `Customers.PoConfig.View` |
| POST `/api/CustomerPoConfig/batch-save` | `[FromBody] CustomerPoConfigBatchSaveRequest req` | `Task<ServiceResult<CustomerPoConfigBatchSaveResponse>>` | `Customers.PoConfig.{Create/Edit/Delete}` |
| GET `/api/CustomerPoConfig/{id}` | `Guid id` | `Task<ServiceResult<CustomerPoConfigDto>>` | `Customers.PoConfig.View` |
| POST `/api/CustomerPoConfig` | `[FromBody] CreateItemRequest<CustomerPoConfigDto> dto` | `Task<ServiceResult<SaveItemResult>>` | `Customers.PoConfig.Create` |
| PUT `/api/CustomerPoConfig/{id}` | `Guid id, [FromBody] UpdateItemRequest<CustomerPoConfigDto> dto` | `Task<ServiceResult<SaveItemResult>>` | `Customers.PoConfig.Edit` |
| DELETE `/api/CustomerPoConfig/{id}` | `DeleteItemRequest id` | `Task<ServiceResult<SaveItemResult>>` | `Customers.PoConfig.Delete` |

契约源码：[CustomerPoConfigBatchSaveResponse](../SwiftExport.Contracts/DTOs/Customers/PoConfigs/Responses/CustomerPoConfigBatchSaveResponse.cs)、[CustomerPoConfigSearchRequest](../SwiftExport.Contracts/DTOs/Customers/PoConfigs/Requests/CustomerPoConfigSearchRequest.cs)、[CustomerPoConfigBatchSaveRequest](../SwiftExport.Contracts/DTOs/Customers/PoConfigs/Requests/CustomerPoConfigBatchSaveRequest.cs)、[CustomerPoConfigDto](../SwiftExport.Contracts/DTOs/Customers/PoConfigs/CustomerPoConfigDto.cs)。

### CustomerProductController

源码：[CustomerProductController](../SwiftExport.WebApi/Controllers/Customers/CustomerProducts/CustomerProductController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| GET `/api/CustomerProduct/{id}` | `Guid id` | `Task<ServiceResult<CustomerProductDto>>` | `Customers.Products.View` |
| POST `/api/CustomerProduct` | `[FromBody] CreateItemRequest<CustomerProductDto> dto` | `Task<ServiceResult<SaveItemResult>>` | `Customers.Products.Create` |
| PUT `/api/CustomerProduct/{id}` | `Guid id, [FromBody] UpdateItemRequest<CustomerProductDto> dto` | `Task<ServiceResult<SaveItemResult>>` | `Customers.Products.Edit` |
| DELETE `/api/CustomerProduct/{id}` | `DeleteItemRequest id` | `Task<ServiceResult<SaveItemResult>>` | `Customers.Products.Delete` |
| POST `/api/CustomerProduct/page` | `[FromBody] CustomerProductSearchRequest request` | `Task<ServiceResult<PagedResponseBase<CustomerProductDto>>>` | `Customers.Products.View` |
| GET `/api/CustomerProduct/customersku/{sku}` | `Guid customerId, string sku` | `Task<ServiceResult<CustomerProductDto>>` | `Customers.Products.View` |
| GET `/api/CustomerProduct/haveproductCustomers` | `无` | `Task<ServiceResult<IReadOnlyList<CustomerDto>>>` | `Customers.Products.View` |
| POST `/api/CustomerProduct/batch-save` | `[FromBody] CustomerProductBatchSaveRequest request` | `Task<ServiceResult<CustomerProductBatchSaveResponse>>` | `Customers.Products.{Create/Edit/Delete}` |

契约源码：[CustomerDto](../SwiftExport.Contracts/DTOs/Customers/Management/CustomerDto.cs)、[CustomerProductBatchSaveResponse](../SwiftExport.Contracts/DTOs/Customers/CustomerProducts/Responses/CustomerProductBatchSaveResponse.cs)、[CustomerProductSearchRequest](../SwiftExport.Contracts/DTOs/Customers/CustomerProducts/Requests/CustomerProductSearchRequest.cs)、[CustomerProductBatchSaveRequest](../SwiftExport.Contracts/DTOs/Customers/CustomerProducts/Requests/CustomerProductBatchSaveRequest.cs)、[CustomerProductDto](../SwiftExport.Contracts/DTOs/Customers/CustomerProducts/CustomerProductDto.cs)。

### DocumentNoController

源码：[DocumentNoController](../SwiftExport.WebApi/Controllers/Systems/Numbering/DocumentNoController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| POST `/api/DocumentNo/next` | `[FromBody] DocumentNoRequest request` | `Task<ServiceResult<DocumentNoResponse>>` | `System.Numbering.Issue` |

契约源码：[DocumentNoResponse](../SwiftExport.Contracts/DTOs/SystemDtos/Numbering/Responses/DocumentNoResponse.cs)、[DocumentNoRequest](../SwiftExport.Contracts/DTOs/SystemDtos/Numbering/Requests/DocumentNoRequest.cs)。

### ExcelReadProfileController

源码：[ExcelReadProfileController](../SwiftExport.WebApi/Controllers/Systems/ExcelReadProfiles/ExcelReadProfileController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| GET `/api/excel-read-profiles` | `[FromQuery] string groupCode, CancellationToken cancellationToken` | `Task<ServiceResult<IReadOnlyList<ExcelReadProfileListItemDto>>>` | `活动登录用户` |
| GET `/api/excel-read-profiles/{uid:guid}` | `Guid uid, CancellationToken cancellationToken` | `Task<ServiceResult<ExcelReadProfileDto>>` | `活动登录用户` |
| POST `/api/excel-read-profiles` | `[FromBody] CreateExcelReadProfileRequest request, CancellationToken cancellationToken` | `Task<ServiceResult<ExcelReadProfileDto>>` | `System.ExcelFieldMapping.Manage` |
| PUT `/api/excel-read-profiles/{uid:guid}` | `Guid uid, [FromBody] UpdateExcelReadProfileRequest request, CancellationToken cancellationToken` | `Task<ServiceResult<ExcelReadProfileDto>>` | `System.ExcelFieldMapping.Manage` |
| DELETE `/api/excel-read-profiles/{uid:guid}` | `Guid uid, [FromQuery] int expectedVersion, CancellationToken cancellationToken` | `Task<ServiceResult<bool>>` | `System.ExcelFieldMapping.Manage` |

契约源码：[UpdateExcelReadProfileRequest](../SwiftExport.Contracts/DTOs/SystemDtos/ExcelReadProfiles/UpdateExcelReadProfileRequest.cs)、[ExcelReadProfileListItemDto](../SwiftExport.Contracts/DTOs/SystemDtos/ExcelReadProfiles/ExcelReadProfileListItemDto.cs)、[ExcelReadProfileDto](../SwiftExport.Contracts/DTOs/SystemDtos/ExcelReadProfiles/ExcelReadProfileDto.cs)、[CreateExcelReadProfileRequest](../SwiftExport.Contracts/DTOs/SystemDtos/ExcelReadProfiles/CreateExcelReadProfileRequest.cs)。

### ExecutionEligibilityController

源码：[ExecutionEligibilityController](../SwiftExport.WebApi/Controllers/MasterData/ExecutionEligibilityController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| POST `/api/master-data/execution-eligibility/status` | ` [FromBody] SetExecutionEligibilityStatusCommand command, CancellationToken cancellationToken` | `Task<ServiceResult<ExecutionEligibilityStatusResult>>` | `MasterData.Eligibility.Manage` |

契约源码：[SetExecutionEligibilityStatusCommand](../SwiftExport.Contracts/DTOs/MasterData/ExecutionEligibility/SetExecutionEligibilityStatusCommand.cs)、[ExecutionEligibilityStatusResult](../SwiftExport.Contracts/DTOs/MasterData/ExecutionEligibility/ExecutionEligibilityStatusResult.cs)。

### FileController

源码：[FileController](../SwiftExport.WebApi/Controllers/Files/FileController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| PUT `/api/files/{containerId}` | ` string containerId, [FromQuery] string path, [FromQuery] bool overwrite = false, CancellationToken cancellationToken = default` | `Task<ActionResult<ServiceResult<FileUploadResultDto>>>` | `System.Files.Manage` |
| GET `/api/files/{containerId}` | ` string containerId, [FromQuery] string path, CancellationToken cancellationToken = default` | `Task<IActionResult>` | `身份专用，见模块说明` |
| HEAD `/api/files/{containerId}` | ` string containerId, [FromQuery] string path, CancellationToken cancellationToken = default` | `Task<IActionResult>` | `身份专用，见模块说明` |

契约源码：[FileUploadResultDto](../SwiftExport.Contracts/DTOs/Files/FileUploadResultDto.cs)。

### ForExcelPackingListController

源码：[ForExcelPackingListController](../SwiftExport.WebApi/Controllers/ForExcel/ForExcelPackingListController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| POST `/api/for-excel/packing-lists/check` | `[FromBody] CheckPackingListImportRequest request, CancellationToken token` | `Task<ServiceResult<CheckPackingListImportResponse>>` | `Shipping.PackingList.Create` |

契约源码：[CheckPackingListImportResponse](../SwiftExport.Contracts/DTOs/ForExcel/PackingLists/CheckPackingListImportResponse.cs)、[CheckPackingListImportRequest](../SwiftExport.Contracts/DTOs/ForExcel/PackingLists/CheckPackingListImportRequest.cs)。

### ForExcelPurchaseOrderController

源码：[ForExcelPurchaseOrderController](../SwiftExport.WebApi/Controllers/ForExcel/ForExcelPurchaseOrderController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| POST `/api/for-excel/purchase-orders/check` | `[FromBody] CheckPurchaseOrderImportRequest request, CancellationToken cancellationToken` | `Task<ServiceResult<CheckPurchaseOrderImportResponse>>` | `未登记，默认拒绝` |

契约源码：[CheckPurchaseOrderImportResponse](../SwiftExport.Contracts/DTOs/ForExcel/PurchaseOrders/CheckPurchaseOrderImportResponse.cs)、[CheckPurchaseOrderImportRequest](../SwiftExport.Contracts/DTOs/ForExcel/PurchaseOrders/CheckPurchaseOrderImportRequest.cs)。

### ForExcelSalesOrderController

源码：[ForExcelSalesOrderController](../SwiftExport.WebApi/Controllers/ForExcel/ForExcelSalesOrderController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| POST `/api/for-excel/sales-orders/check` | `[FromBody] CheckSalesOrderImportRequest request, CancellationToken cancellationToken` | `Task<ServiceResult<CheckSalesOrderImportResponse>>` | `Sales.Order.Maintain` |

契约源码：[CheckSalesOrderImportResponse](../SwiftExport.Contracts/DTOs/ForExcel/SalesOrders/CheckSalesOrderImportResponse.cs)、[CheckSalesOrderImportRequest](../SwiftExport.Contracts/DTOs/ForExcel/SalesOrders/CheckSalesOrderImportRequest.cs)。

### GrossDemandQueryController

源码：[GrossDemandQueryController](../SwiftExport.WebApi/Controllers/DemandPlanning/Sources/Query/GrossDemandQueryController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| POST `/api/demand-planning/gross-demands/search` | ` [FromBody] GrossDemandSearchRequest request, CancellationToken cancellationToken` | `Task<ServiceResult<PagedResponseBase<GrossDemandListItemDto>>>` | `DemandPlanning.Source.View` |
| POST `/api/demand-planning/gross-demands/source-groups/search` | ` [FromBody] GrossDemandSourceGroupSearchRequest request, CancellationToken cancellationToken` | `Task<ServiceResult<List<GrossDemandSourceGroupDto>>>` | `DemandPlanning.Source.View` |
| GET `/api/demand-planning/gross-demands/{uid:guid}` | `[FromRoute] Guid uid, CancellationToken cancellationToken` | `Task<ServiceResult<GrossDemandDetailDto>>` | `DemandPlanning.Source.View` |
| GET `/api/demand-planning/gross-demands/{uid:guid}/trace` | `[FromRoute] Guid uid, CancellationToken cancellationToken` | `Task<ServiceResult<GrossDemandTraceDetailDto>>` | `DemandPlanning.Source.View` |

契约源码：[GrossDemandTraceDetailDto](../SwiftExport.Contracts/DTOs/DemandPlanning/GrossDemand/Trace/GrossDemandTraceDetailDto.cs)、[GrossDemandDetailDto](../SwiftExport.Contracts/DTOs/DemandPlanning/GrossDemand/Query/GrossDemandDetailDto.cs)、[GrossDemandSourceGroupSearchRequest](../SwiftExport.Contracts/DTOs/DemandPlanning/GrossDemand/Query/GrossDemandSourceGroupSearchRequest.cs)、[GrossDemandSourceGroupDto](../SwiftExport.Contracts/DTOs/DemandPlanning/GrossDemand/Query/GrossDemandSourceGroupDto.cs)、[GrossDemandSearchRequest](../SwiftExport.Contracts/DTOs/DemandPlanning/GrossDemand/Query/GrossDemandSearchRequest.cs)、[GrossDemandListItemDto](../SwiftExport.Contracts/DTOs/DemandPlanning/GrossDemand/Query/GrossDemandListItemDto.cs)。

### InboundDraftController

源码：[InboundDraftController](../SwiftExport.WebApi/Controllers/Inventory/Inbounds/InboundDraftController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| POST `/api/inventory/inbounds/drafts` | `[FromBody] CreateInboundDraftCommand command, CancellationToken token` | `Task<ServiceResult<InboundDraftCommandResult>>` | `Inventory.Inbound.Create` |
| PUT `/api/inventory/inbounds/drafts/{uid:guid}` | `Guid uid, [FromBody] UpdateInboundDraftCommand command, CancellationToken token` | `Task<ServiceResult<InboundDraftCommandResult>>` | `Inventory.Inbound.Edit` |
| POST `/api/inventory/inbounds/drafts/{uid:guid}/delete` | `Guid uid, [FromBody] DeleteInboundDraftCommand command, CancellationToken token` | `Task<ServiceResult<InboundDraftCommandResult>>` | `Inventory.Inbound.Delete` |

契约源码：[CreateInboundDraftCommand](../SwiftExport.Contracts/DTOs/Inventory/Inbounds/Draft/CreateInboundDraftCommand.cs)、[InboundDraftCommandResult](../SwiftExport.Contracts/DTOs/Inventory/Inbounds/Draft/InboundDraftCommandResult.cs)、[DeleteInboundDraftCommand](../SwiftExport.Contracts/DTOs/Inventory/Inbounds/Draft/DeleteInboundDraftCommand.cs)、[UpdateInboundDraftCommand](../SwiftExport.Contracts/DTOs/Inventory/Inbounds/Draft/UpdateInboundDraftCommand.cs)。

### InboundPostingController

源码：[InboundPostingController](../SwiftExport.WebApi/Controllers/Inventory/Inbounds/InboundPostingController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| POST `/api/inventory/inbounds/{uid:guid}/post-preview` | `Guid uid, [FromBody] InboundPostPreviewRequest request, CancellationToken token` | `Task<ServiceResult<InboundPostPreviewDto>>` | `Inventory.Inbound.Post` |
| POST `/api/inventory/inbounds/{uid:guid}/post` | `Guid uid, [FromBody] PostInboundCommand command, CancellationToken token` | `Task<ServiceResult<InboundPostResult>>` | `Inventory.Inbound.Post` |

契约源码：[PostInboundCommand](../SwiftExport.Contracts/DTOs/Inventory/Inbounds/Posting/PostInboundCommand.cs)、[InboundPostPreviewRequest](../SwiftExport.Contracts/DTOs/Inventory/Inbounds/Posting/InboundPostPreviewRequest.cs)、[InboundPostResult](../SwiftExport.Contracts/DTOs/Inventory/Inbounds/Posting/InboundPostResult.cs)、[InboundPostPreviewDto](../SwiftExport.Contracts/DTOs/Inventory/Inbounds/Posting/InboundPostPreviewDto.cs)。

### InboundQueryController

源码：[InboundQueryController](../SwiftExport.WebApi/Controllers/Inventory/Inbounds/InboundQueryController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| POST `/api/inventory/inbounds/search` | `[FromBody] InboundManagerSearchRequest request, CancellationToken token` | `Task<ServiceResult<PagedResponseBase<InboundManagerListItemDto>>>` | `Inventory.Inbound.View` |
| GET `/api/inventory/inbounds/{uid:guid}` | `Guid uid, CancellationToken token` | `Task<ServiceResult<InboundManagerDetailDto>>` | `Inventory.Inbound.View` |

契约源码：[InboundManagerSearchRequest](../SwiftExport.Contracts/DTOs/Inventory/Inbounds/Query/InboundManagerSearchRequest.cs)、[InboundManagerListItemDto](../SwiftExport.Contracts/DTOs/Inventory/Inbounds/Query/InboundManagerListItemDto.cs)、[InboundManagerDetailDto](../SwiftExport.Contracts/DTOs/Inventory/Inbounds/Query/InboundManagerDetailDto.cs)。

### InboundReversalController

源码：[InboundReversalController](../SwiftExport.WebApi/Controllers/Inventory/Inbounds/InboundReversalController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| POST `/api/inventory/inbounds/{uid:guid}/reverse-preview` | `Guid uid, [FromBody] InboundReversePreviewRequest request, CancellationToken token` | `Task<ServiceResult<InboundReversePreviewDto>>` | `Inventory.Inbound.Reverse` |
| POST `/api/inventory/inbounds/{uid:guid}/reverse` | `Guid uid, [FromBody] ReverseInboundCommand command, CancellationToken token` | `Task<ServiceResult<InboundReverseResult>>` | `Inventory.Inbound.Reverse` |

契约源码：[ReverseInboundCommand](../SwiftExport.Contracts/DTOs/Inventory/Inbounds/Reversal/ReverseInboundCommand.cs)、[InboundReverseResult](../SwiftExport.Contracts/DTOs/Inventory/Inbounds/Reversal/InboundReverseResult.cs)、[InboundReversePreviewRequest](../SwiftExport.Contracts/DTOs/Inventory/Inbounds/Reversal/InboundReversePreviewRequest.cs)、[InboundReversePreviewDto](../SwiftExport.Contracts/DTOs/Inventory/Inbounds/Reversal/InboundReversePreviewDto.cs)。

### InboundSourcesController

源码：[InboundSourcesController](../SwiftExport.WebApi/Controllers/Inventory/Inbounds/InboundSourcesController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| POST `/api/inventory/inbound-sources/search` | `[FromBody] InboundSourceSearchRequest request, CancellationToken token` | `Task<ServiceResult<PagedResponseBase<InboundSourceCandidateDto>>>` | `Inventory.Inbound.View` |

契约源码：[InboundSourceSearchRequest](../SwiftExport.Contracts/DTOs/Inventory/Inbounds/Sources/InboundSourceSearchRequest.cs)、[InboundSourceCandidateDto](../SwiftExport.Contracts/DTOs/Inventory/Inbounds/Sources/InboundSourceCandidateDto.cs)。

### InventoryController

源码：[InventoryController](../SwiftExport.WebApi/Controllers/Inventory/Query/InventoryController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| POST `/api/inventory/balances/search` | `[FromBody] InventoryBalanceSearchRequest request, CancellationToken cancellationToken` | `Task<ServiceResult<PagedResponseBase<InventoryBalanceDto>>>` | `Inventory.Balance.View` |
| POST `/api/inventory/ledger/search` | `[FromBody] InventoryLedgerSearchRequest request, CancellationToken cancellationToken` | `Task<ServiceResult<PagedResponseBase<InventoryLedgerItemDto>>>` | `Inventory.Balance.View` |
| GET `/api/inventory/balances/{uid:guid}/trace` | `Guid uid, [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default` | `Task<ServiceResult<InventoryBalanceTraceDto>>` | `Inventory.Balance.View` |

契约源码：[InventoryBalanceSearchRequest](../SwiftExport.Contracts/DTOs/Inventory/Balances/InventoryBalanceSearchRequest.cs)、[InventoryBalanceDto](../SwiftExport.Contracts/DTOs/Inventory/Balances/InventoryBalanceDto.cs)、[InventoryLedgerSearchRequest](../SwiftExport.Contracts/DTOs/Inventory/Ledger/InventoryLedgerSearchRequest.cs)、[InventoryLedgerItemDto](../SwiftExport.Contracts/DTOs/Inventory/Ledger/InventoryLedgerItemDto.cs)、[InventoryBalanceTraceDto](../SwiftExport.Contracts/DTOs/Inventory/Trace/InventoryBalanceTraceDto.cs)。

### ManualGrossDemandController

源码：[ManualGrossDemandController](../SwiftExport.WebApi/Controllers/DemandPlanning/Sources/Manual/ManualGrossDemandController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| POST `/api/demand-planning/gross-demands/manual` | `[FromBody] CreateManualGrossDemandRequest request` | `Task<ServiceResult<GrossDemandDetailDto>>` | `DemandPlanning.Source.Manual.Manage` |
| PUT `/api/demand-planning/gross-demands/manual/{uid:guid}` | ` [FromRoute] Guid uid, [FromBody] UpdateManualGrossDemandRequest request` | `Task<ServiceResult<GrossDemandDetailDto>>` | `DemandPlanning.Source.Manual.Manage` |
| POST `/api/demand-planning/gross-demands/manual/{uid:guid}/close` | ` [FromRoute] Guid uid, [FromBody] CloseGrossDemandCommand command` | `Task<ServiceResult<GrossDemandDetailDto>>` | `DemandPlanning.Source.Manual.Manage` |

契约源码：[GrossDemandDetailDto](../SwiftExport.Contracts/DTOs/DemandPlanning/GrossDemand/Query/GrossDemandDetailDto.cs)、[UpdateManualGrossDemandRequest](../SwiftExport.Contracts/DTOs/DemandPlanning/GrossDemand/Manual/UpdateManualGrossDemandRequest.cs)、[CreateManualGrossDemandRequest](../SwiftExport.Contracts/DTOs/DemandPlanning/GrossDemand/Manual/CreateManualGrossDemandRequest.cs)、[CloseGrossDemandCommand](../SwiftExport.Contracts/DTOs/DemandPlanning/GrossDemand/Manual/CloseGrossDemandCommand.cs)。

### MaterialDemandEstablishmentController

源码：[MaterialDemandEstablishmentController](../SwiftExport.WebApi/Controllers/DemandPlanning/Materials/Establishment/MaterialDemandEstablishmentController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| POST `/api/demand-planning/net-demands/source-demands/establish` | ` [FromBody] EstablishMaterialDemandsCommand command, CancellationToken cancellationToken` | `Task<ServiceResult<EstablishMaterialDemandsResult>>` | `DemandPlanning.Material.Establish` |

契约源码：[EstablishMaterialDemandsResult](../SwiftExport.Contracts/DTOs/DemandPlanning/NetDemand/Establishment/EstablishMaterialDemandsResult.cs)、[EstablishMaterialDemandsCommand](../SwiftExport.Contracts/DTOs/DemandPlanning/NetDemand/Establishment/EstablishMaterialDemandsCommand.cs)。

### MaterialDemandQueryController

源码：[MaterialDemandQueryController](../SwiftExport.WebApi/Controllers/DemandPlanning/Materials/Query/MaterialDemandQueryController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| GET `/api/demand-planning/net-demands/{uid:guid}/trace` | `[FromRoute] Guid uid, CancellationToken cancellationToken` | `Task<ServiceResult<MaterialDemandTraceDetailDto>>` | `DemandPlanning.Material.View` |
| POST `/api/demand-planning/net-demands/material-search` | ` [FromBody] MaterialDemandManagerSearchRequest request, CancellationToken cancellationToken` | `Task<ServiceResult<PagedResponseBase<MaterialDemandManagerItemDto>>>` | `DemandPlanning.Material.View` |
| POST `/api/demand-planning/net-demands/material-source-groups` | ` [FromBody] MaterialDemandSourceGroupSearchRequest request, CancellationToken cancellationToken` | `Task<ServiceResult<List<NetDemandSourceGroupDto>>>` | `DemandPlanning.Material.View` |
| GET `/api/demand-planning/net-demands/source-demands/candidate-groups` | ` CancellationToken cancellationToken` | `Task<ServiceResult<List<SourceDemandCandidateGroupDto>>>` | `DemandPlanning.Material.View` |
| GET `/api/demand-planning/net-demands/source-demands/candidates/{sourceType}/{sourceUid:guid}` | ` string sourceType, Guid sourceUid, CancellationToken cancellationToken` | `Task<ServiceResult<List<SourceDemandCandidateDto>>>` | `DemandPlanning.Material.View` |

契约源码：[MaterialDemandTraceDetailDto](../SwiftExport.Contracts/DTOs/DemandPlanning/NetDemand/Trace/MaterialDemandTraceDetailDto.cs)、[NetDemandSourceGroupDto](../SwiftExport.Contracts/DTOs/DemandPlanning/NetDemand/Query/NetDemandSourceGroupDto.cs)、[MaterialDemandSourceGroupSearchRequest](../SwiftExport.Contracts/DTOs/DemandPlanning/NetDemand/Query/MaterialDemandSourceGroupSearchRequest.cs)、[MaterialDemandManagerSearchRequest](../SwiftExport.Contracts/DTOs/DemandPlanning/NetDemand/Query/MaterialDemandManagerSearchRequest.cs)、[MaterialDemandManagerItemDto](../SwiftExport.Contracts/DTOs/DemandPlanning/NetDemand/Query/MaterialDemandManagerItemDto.cs)、[SourceDemandCandidateGroupDto](../SwiftExport.Contracts/DTOs/DemandPlanning/NetDemand/Sources/SourceDemandCandidateGroupDto.cs)、[SourceDemandCandidateDto](../SwiftExport.Contracts/DTOs/DemandPlanning/NetDemand/Sources/SourceDemandCandidateDto.cs)。

### MaterialDemandReversalController

源码：[MaterialDemandReversalController](../SwiftExport.WebApi/Controllers/DemandPlanning/Materials/Reversal/MaterialDemandReversalController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| POST `/api/demand-planning/net-demands/source-demands/rollback` | ` [FromBody] RollbackMaterialDemandsCommand command, CancellationToken cancellationToken` | `Task<ServiceResult<RollbackMaterialDemandsResult>>` | `DemandPlanning.Material.Rollback` |

契约源码：[RollbackMaterialDemandsResult](../SwiftExport.Contracts/DTOs/DemandPlanning/NetDemand/Reversal/RollbackMaterialDemandsResult.cs)、[RollbackMaterialDemandsCommand](../SwiftExport.Contracts/DTOs/DemandPlanning/NetDemand/Reversal/RollbackMaterialDemandsCommand.cs)。

### MaterialDemandSupplierSuggestionController

源码：[MaterialDemandSupplierSuggestionController](../SwiftExport.WebApi/Controllers/DemandPlanning/Materials/SupplierSuggestion/MaterialDemandSupplierSuggestionController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| POST `/api/demand-planning/net-demands/suggested-purchase-supplier` | ` [FromBody] UpdateSuggestedPurchaseSupplierCommand command, CancellationToken cancellationToken` | `Task<ServiceResult<UpdateSuggestedPurchaseSupplierResult>>` | `DemandPlanning.Material.SupplierSuggestion.Manage` |

契约源码：[UpdateSuggestedPurchaseSupplierResult](../SwiftExport.Contracts/DTOs/DemandPlanning/NetDemand/SupplierSuggestion/UpdateSuggestedPurchaseSupplierResult.cs)、[UpdateSuggestedPurchaseSupplierCommand](../SwiftExport.Contracts/DTOs/DemandPlanning/NetDemand/SupplierSuggestion/UpdateSuggestedPurchaseSupplierCommand.cs)。

### OutboundDraftController

源码：[OutboundDraftController](../SwiftExport.WebApi/Controllers/Inventory/Outbounds/OutboundDraftController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| POST `/api/inventory/outbounds/drafts` | `[FromBody] CreateOutboundDraftCommand command, CancellationToken token` | `Task<ServiceResult<OutboundDraftCommandResult>>` | `Inventory.Outbound.Create` |
| PUT `/api/inventory/outbounds/drafts/{uid:guid}` | `Guid uid, [FromBody] UpdateOutboundDraftCommand command, CancellationToken token` | `Task<ServiceResult<OutboundDraftCommandResult>>` | `Inventory.Outbound.Edit` |
| POST `/api/inventory/outbounds/drafts/{uid:guid}/delete` | `Guid uid, [FromBody] DeleteOutboundDraftCommand command, CancellationToken token` | `Task<ServiceResult<OutboundDraftCommandResult>>` | `Inventory.Outbound.Delete` |

契约源码：[UpdateOutboundDraftCommand](../SwiftExport.Contracts/DTOs/Inventory/Outbounds/Draft/UpdateOutboundDraftCommand.cs)、[OutboundDraftCommandResult](../SwiftExport.Contracts/DTOs/Inventory/Outbounds/Draft/OutboundDraftCommandResult.cs)、[DeleteOutboundDraftCommand](../SwiftExport.Contracts/DTOs/Inventory/Outbounds/Draft/DeleteOutboundDraftCommand.cs)、[CreateOutboundDraftCommand](../SwiftExport.Contracts/DTOs/Inventory/Outbounds/Draft/CreateOutboundDraftCommand.cs)。

### OutboundPostingController

源码：[OutboundPostingController](../SwiftExport.WebApi/Controllers/Inventory/Outbounds/OutboundPostingController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| POST `/api/inventory/outbounds/{uid:guid}/post-preview` | `Guid uid, [FromBody] OutboundPostPreviewRequest request, CancellationToken token` | `Task<ServiceResult<OutboundPostPreviewDto>>` | `Inventory.Outbound.Post` |
| POST `/api/inventory/outbounds/{uid:guid}/post` | `Guid uid, [FromBody] PostOutboundCommand command, CancellationToken token` | `Task<ServiceResult<OutboundPostResult>>` | `Inventory.Outbound.Post` |

契约源码：[PostOutboundCommand](../SwiftExport.Contracts/DTOs/Inventory/Outbounds/Posting/PostOutboundCommand.cs)、[OutboundPostResult](../SwiftExport.Contracts/DTOs/Inventory/Outbounds/Posting/OutboundPostResult.cs)、[OutboundPostPreviewRequest](../SwiftExport.Contracts/DTOs/Inventory/Outbounds/Posting/OutboundPostPreviewRequest.cs)、[OutboundPostPreviewDto](../SwiftExport.Contracts/DTOs/Inventory/Outbounds/Posting/OutboundPostPreviewDto.cs)。

### OutboundQueryController

源码：[OutboundQueryController](../SwiftExport.WebApi/Controllers/Inventory/Outbounds/OutboundQueryController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| POST `/api/inventory/outbounds/items/search` | `[FromBody] OutboundItemSearchRequest request, CancellationToken token` | `Task<ServiceResult<PagedResponseBase<OutboundItemCandidateDto>>>` | `Inventory.Outbound.View` |
| POST `/api/inventory/outbounds/search` | `[FromBody] OutboundManagerSearchRequest request, CancellationToken token` | `Task<ServiceResult<PagedResponseBase<OutboundManagerListItemDto>>>` | `Inventory.Outbound.View` |
| GET `/api/inventory/outbounds/{uid:guid}` | `Guid uid, CancellationToken token` | `Task<ServiceResult<OutboundManagerDetailDto>>` | `Inventory.Outbound.View` |

契约源码：[OutboundItemSearchRequest](../SwiftExport.Contracts/DTOs/Inventory/Outbounds/Query/OutboundItemSearchRequest.cs)、[OutboundItemCandidateDto](../SwiftExport.Contracts/DTOs/Inventory/Outbounds/Query/OutboundItemCandidateDto.cs)、[OutboundManagerDetailDto](../SwiftExport.Contracts/DTOs/Inventory/Outbounds/Query/OutboundManagerDetailDto.cs)、[OutboundManagerSearchRequest](../SwiftExport.Contracts/DTOs/Inventory/Outbounds/Query/OutboundManagerSearchRequest.cs)、[OutboundManagerListItemDto](../SwiftExport.Contracts/DTOs/Inventory/Outbounds/Query/OutboundManagerListItemDto.cs)。

### OutboundReversalController

源码：[OutboundReversalController](../SwiftExport.WebApi/Controllers/Inventory/Outbounds/OutboundReversalController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| POST `/api/inventory/outbounds/{uid:guid}/reverse-preview` | `Guid uid, [FromBody] OutboundReversePreviewRequest request, CancellationToken token` | `Task<ServiceResult<OutboundReversePreviewDto>>` | `Inventory.Outbound.Reverse` |
| POST `/api/inventory/outbounds/{uid:guid}/reverse` | `Guid uid, [FromBody] ReverseOutboundCommand command, CancellationToken token` | `Task<ServiceResult<OutboundReverseResult>>` | `Inventory.Outbound.Reverse` |

契约源码：[ReverseOutboundCommand](../SwiftExport.Contracts/DTOs/Inventory/Outbounds/Reversal/ReverseOutboundCommand.cs)、[OutboundReverseResult](../SwiftExport.Contracts/DTOs/Inventory/Outbounds/Reversal/OutboundReverseResult.cs)、[OutboundReversePreviewRequest](../SwiftExport.Contracts/DTOs/Inventory/Outbounds/Reversal/OutboundReversePreviewRequest.cs)、[OutboundReversePreviewDto](../SwiftExport.Contracts/DTOs/Inventory/Outbounds/Reversal/OutboundReversePreviewDto.cs)。

### PackingListController

源码：[PackingListController](../SwiftExport.WebApi/Controllers/Shipping/PackingLists/PackingListController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| POST `/api/shipping/packing-lists/drafts/{uid:guid}/lines/{lineUid:guid}/associate-sources` | `Guid uid, Guid lineUid, [FromBody] AssociatePackingListSourcesCommand command, CancellationToken token` | `Task<ActionResult<ServiceResult<PackingListDraftCommandResult>>>` | `Shipping.PackingList.Edit` |
| POST `/api/shipping/packing-lists/{uid:guid}/post-preview` | `Guid uid, [FromBody] PackingListPostPreviewRequest request, CancellationToken token` | `Task<ActionResult<ServiceResult<PackingListPostingPreviewDto>>>` | `Shipping.PackingList.Post` |
| POST `/api/shipping/packing-lists/{uid:guid}/post` | `Guid uid, [FromBody] PostPackingListCommand request, CancellationToken token` | `Task<ActionResult<ServiceResult<PackingListPostingCommandResult>>>` | `Shipping.PackingList.Post` |
| POST `/api/shipping/packing-lists/{uid:guid}/reverse-preview` | `Guid uid, [FromBody] PackingListReversePreviewRequest request, CancellationToken token` | `Task<ActionResult<ServiceResult<PackingListPostingPreviewDto>>>` | `Shipping.PackingList.Reverse` |
| POST `/api/shipping/packing-lists/{uid:guid}/reverse` | `Guid uid, [FromBody] ReversePackingListCommand request, CancellationToken token` | `Task<ActionResult<ServiceResult<PackingListPostingCommandResult>>>` | `Shipping.PackingList.Reverse` |
| GET `/api/shipping/packing-lists/{uid:guid}/trace` | `Guid uid, CancellationToken token` | `Task<ActionResult<ServiceResult<PackingListTraceDto>>>` | `Shipping.PackingList.View` |
| POST `/api/shipping/packing-lists/search` | ` [FromBody] PackingListManagerSearchRequest request, CancellationToken token` | `Task<ActionResult<ServiceResult<PagedResponseBase<PackingListManagerListItemDto>>>>` | `Shipping.PackingList.View` |
| GET `/api/shipping/packing-lists/{uid:guid}` | `Guid uid, CancellationToken token` | `Task<ActionResult<ServiceResult<PackingListDetailDto>>>` | `Shipping.PackingList.View` |
| POST `/api/shipping/packing-lists/sources/search` | ` [FromBody] PackingListSourceSearchRequest request, CancellationToken token` | `Task<ActionResult<ServiceResult<PagedResponseBase<PackingListSourceCandidateDto>>>>` | `Shipping.PackingList.View` |
| POST `/api/shipping/packing-lists/drafts` | ` [FromBody] CreatePackingListDraftCommand command, CancellationToken token` | `Task<ActionResult<ServiceResult<PackingListDraftCommandResult>>>` | `Shipping.PackingList.Create` |
| PUT `/api/shipping/packing-lists/drafts/{uid:guid}` | ` Guid uid, [FromBody] UpdatePackingListDraftCommand command, CancellationToken token` | `Task<ActionResult<ServiceResult<PackingListDraftCommandResult>>>` | `Shipping.PackingList.Edit` |
| POST `/api/shipping/packing-lists/drafts/{uid:guid}/delete` | ` Guid uid, [FromBody] DeletePackingListDraftCommand command, CancellationToken token` | `Task<ActionResult<ServiceResult<PackingListDraftCommandResult>>>` | `Shipping.PackingList.Delete` |

契约源码：[PackingListTraceDto](../SwiftExport.Contracts/DTOs/Shipping/PackingLists/Trace/PackingListTraceDto.cs)、[UpdatePackingListDraftCommand](../SwiftExport.Contracts/DTOs/Shipping/PackingLists/Draft/UpdatePackingListDraftCommand.cs)、[PackingListDraftCommandResult](../SwiftExport.Contracts/DTOs/Shipping/PackingLists/Draft/PackingListDraftCommandResult.cs)、[DeletePackingListDraftCommand](../SwiftExport.Contracts/DTOs/Shipping/PackingLists/Draft/DeletePackingListDraftCommand.cs)、[CreatePackingListDraftCommand](../SwiftExport.Contracts/DTOs/Shipping/PackingLists/Draft/CreatePackingListDraftCommand.cs)、[AssociatePackingListSourcesCommand](../SwiftExport.Contracts/DTOs/Shipping/PackingLists/Draft/AssociatePackingListSourcesCommand.cs)、[ReversePackingListCommand](../SwiftExport.Contracts/DTOs/Shipping/PackingLists/Reversal/ReversePackingListCommand.cs)、[PackingListReversePreviewRequest](../SwiftExport.Contracts/DTOs/Shipping/PackingLists/Reversal/PackingListReversePreviewRequest.cs)、[PackingListSourceSearchRequest](../SwiftExport.Contracts/DTOs/Shipping/PackingLists/Query/PackingListSourceSearchRequest.cs)、[PackingListSourceCandidateDto](../SwiftExport.Contracts/DTOs/Shipping/PackingLists/Query/PackingListSourceCandidateDto.cs)、[PackingListManagerSearchRequest](../SwiftExport.Contracts/DTOs/Shipping/PackingLists/Query/PackingListManagerSearchRequest.cs)、[PackingListManagerListItemDto](../SwiftExport.Contracts/DTOs/Shipping/PackingLists/Query/PackingListManagerListItemDto.cs)、[PackingListDetailDto](../SwiftExport.Contracts/DTOs/Shipping/PackingLists/Query/PackingListDetailDto.cs)、[PostPackingListCommand](../SwiftExport.Contracts/DTOs/Shipping/PackingLists/Posting/PostPackingListCommand.cs)、[PackingListPostPreviewRequest](../SwiftExport.Contracts/DTOs/Shipping/PackingLists/Posting/PackingListPostPreviewRequest.cs)、[PackingListPostingPreviewDto](../SwiftExport.Contracts/DTOs/Shipping/PackingLists/Posting/PackingListPostingPreviewDto.cs)、[PackingListPostingCommandResult](../SwiftExport.Contracts/DTOs/Shipping/PackingLists/Posting/PackingListPostingCommandResult.cs)。

### ProcurementDemandAssociationController

源码：[ProcurementDemandAssociationController](../SwiftExport.WebApi/Controllers/Procurement/PurchaseOrders/Association/ProcurementDemandAssociationController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| GET `/api/procurement/orders/{uid}/demand-association-context` | `Guid uid, CancellationToken cancellationToken = default` | `Task<ServiceResult<ProcurementDemandAssociationContextDto>>` | `未登记，默认拒绝` |
| POST `/api/procurement/orders/{uid}/demand-association-candidates` | `Guid uid, [FromBody] ProcurementDemandCandidateSearchRequest request, CancellationToken cancellationToken = default` | `Task<ServiceResult<PagedResponseBase<ProcurementDemandCandidateDto>>>` | `未登记，默认拒绝` |
| POST `/api/procurement/orders/{uid}/demand-association-preview` | `Guid uid, [FromBody] PreviewProcurementDemandAssociationRequest request, CancellationToken cancellationToken = default` | `Task<ServiceResult<ProcurementDemandAssociationPreviewDto>>` | `未登记，默认拒绝` |
| POST `/api/procurement/orders/{uid}/associate-demands` | `Guid uid, [FromBody] AssociateProcurementDemandsCommand command, CancellationToken cancellationToken = default` | `Task<ServiceResult<ProcurementOrderCommandResult>>` | `未登记，默认拒绝` |

契约源码：[ProcurementOrderCommandResult](../SwiftExport.Contracts/DTOs/Procurement/PurchaseOrders/ProcurementOrderCommandResult.cs)、[ProcurementDemandAssociationContextDto](../SwiftExport.Contracts/DTOs/Procurement/PurchaseOrders/Association/ProcurementDemandAssociationContextDto.cs)、[PreviewProcurementDemandAssociationRequest](../SwiftExport.Contracts/DTOs/Procurement/PurchaseOrders/Association/PreviewProcurementDemandAssociationRequest.cs)、[AssociateProcurementDemandsCommand](../SwiftExport.Contracts/DTOs/Procurement/PurchaseOrders/Association/AssociateProcurementDemandsCommand.cs)、[ProcurementDemandCandidateSearchRequest](../SwiftExport.Contracts/DTOs/Procurement/PurchaseOrders/Association/ProcurementDemandCandidateSearchRequest.cs)、[ProcurementDemandCandidateDto](../SwiftExport.Contracts/DTOs/Procurement/PurchaseOrders/Association/ProcurementDemandCandidateDto.cs)、[ProcurementDemandAssociationPreviewDto](../SwiftExport.Contracts/DTOs/Procurement/PurchaseOrders/Association/ProcurementDemandAssociationPreviewDto.cs)。

### ProcurementMasterDataQueryController

源码：[ProcurementMasterDataQueryController](../SwiftExport.WebApi/Controllers/MasterData/ProcurementMasterDataQueryController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| POST `/api/master-data/procurement-lookups/customers/candidates/search` | ` [FromBody] PagedRequestBase request, CancellationToken cancellationToken = default` | `Task<ActionResult<ServiceResult<PagedResponseBase<ProcurementCustomerCandidateDto>>>>` | `Procurement.Lookup.View` |
| POST `/api/master-data/procurement-lookups/suppliers/candidates/search` | ` [FromBody] PagedRequestBase request, CancellationToken cancellationToken = default` | `Task<ActionResult<ServiceResult<PagedResponseBase<ProcurementSupplierCandidateDto>>>>` | `Procurement.Lookup.View` |
| POST `/api/master-data/procurement-lookups/products/candidates/search` | ` [FromBody] ProcurementProductSearchRequest request, CancellationToken cancellationToken = default` | `Task<ActionResult<ServiceResult<PagedResponseBase<ProcurementProductCandidateDto>>>>` | `Procurement.Lookup.View` |
| POST `/api/master-data/procurement-lookups/customers/history-filter/search` | ` [FromBody] PagedRequestBase request, CancellationToken cancellationToken = default` | `Task<ActionResult<ServiceResult<PagedResponseBase<ProcurementCustomerFilterDto>>>>` | `Procurement.Lookup.View` |
| POST `/api/master-data/procurement-lookups/suppliers/history-filter/search` | ` [FromBody] PagedRequestBase request, CancellationToken cancellationToken = default` | `Task<ActionResult<ServiceResult<PagedResponseBase<ProcurementSupplierFilterDto>>>>` | `Procurement.Lookup.View` |

契约源码：[ProcurementSupplierFilterDto](../SwiftExport.Contracts/DTOs/MasterData/ProcurementLookups/ProcurementSupplierFilterDto.cs)、[ProcurementSupplierCandidateDto](../SwiftExport.Contracts/DTOs/MasterData/ProcurementLookups/ProcurementSupplierCandidateDto.cs)、[ProcurementProductSearchRequest](../SwiftExport.Contracts/DTOs/MasterData/ProcurementLookups/ProcurementProductSearchRequest.cs)、[ProcurementProductCandidateDto](../SwiftExport.Contracts/DTOs/MasterData/ProcurementLookups/ProcurementProductCandidateDto.cs)、[ProcurementCustomerFilterDto](../SwiftExport.Contracts/DTOs/MasterData/ProcurementLookups/ProcurementCustomerFilterDto.cs)、[ProcurementCustomerCandidateDto](../SwiftExport.Contracts/DTOs/MasterData/ProcurementLookups/ProcurementCustomerCandidateDto.cs)。

### ProcurementOrderConfirmationController

源码：[ProcurementOrderConfirmationController](../SwiftExport.WebApi/Controllers/Procurement/PurchaseOrders/Confirmation/ProcurementOrderConfirmationController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| GET `/api/procurement/orders/{uid}/confirmation-context` | `Guid uid, CancellationToken cancellationToken = default` | `Task<ServiceResult<ProcurementConfirmationContextDto>>` | `Procurement.Order.Confirm` |
| POST `/api/procurement/orders/{uid}/confirm` | `Guid uid, [FromBody] ConfirmProcurementOrderCommand command, CancellationToken cancellationToken = default` | `Task<ServiceResult<ProcurementOrderCommandResult>>` | `Procurement.Order.Confirm` |

契约源码：[ProcurementOrderCommandResult](../SwiftExport.Contracts/DTOs/Procurement/PurchaseOrders/ProcurementOrderCommandResult.cs)、[ProcurementConfirmationContextDto](../SwiftExport.Contracts/DTOs/Procurement/PurchaseOrders/Confirmation/ProcurementConfirmationContextDto.cs)、[ConfirmProcurementOrderCommand](../SwiftExport.Contracts/DTOs/Procurement/PurchaseOrders/Confirmation/ConfirmProcurementOrderCommand.cs)。

### ProcurementOrderDraftController

源码：[ProcurementOrderDraftController](../SwiftExport.WebApi/Controllers/Procurement/PurchaseOrders/Draft/ProcurementOrderDraftController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| POST `/api/procurement/orders` | `[FromBody] CreateIndependentPurchaseOrderCommand command, CancellationToken cancellationToken = default` | `Task<ServiceResult<ProcurementOrderCommandResult>>` | `Procurement.Order.Create` |
| PUT `/api/procurement/orders/{uid}/commercial` | `Guid uid, [FromBody] UpdatePurchaseOrderDraftCommand command, CancellationToken cancellationToken = default` | `Task<ServiceResult<ProcurementOrderCommandResult>>` | `Procurement.Order.Edit` |

契约源码：[ProcurementOrderCommandResult](../SwiftExport.Contracts/DTOs/Procurement/PurchaseOrders/ProcurementOrderCommandResult.cs)、[CreateIndependentPurchaseOrderCommand](../SwiftExport.Contracts/DTOs/Procurement/PurchaseOrders/Draft/CreateIndependentPurchaseOrderCommand.cs)、[UpdatePurchaseOrderDraftCommand](../SwiftExport.Contracts/DTOs/Procurement/PurchaseOrders/Query/UpdatePurchaseOrderDraftCommand.cs)。

### ProcurementOrderQueryController

源码：[ProcurementOrderQueryController](../SwiftExport.WebApi/Controllers/Procurement/PurchaseOrders/Query/ProcurementOrderQueryController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| POST `/api/procurement/orders/search` | `ProcurementOrderSearchRequest request, CancellationToken cancellationToken = default` | `Task<ServiceResult<PagedResponseBase<ProcurementOrderListItemDto>>>` | `Procurement.Order.Read` |
| GET `/api/procurement/orders/{uid}` | `Guid uid, CancellationToken cancellationToken = default` | `Task<ServiceResult<PurchaseOrderDetailDto>>` | `Procurement.Order.Read` |

契约源码：[PurchaseOrderDetailDto](../SwiftExport.Contracts/DTOs/Procurement/PurchaseOrders/Query/PurchaseOrderDetailDto.cs)、[ProcurementOrderSearchRequest](../SwiftExport.Contracts/DTOs/Procurement/PurchaseOrders/Query/ProcurementOrderSearchRequest.cs)、[ProcurementOrderListItemDto](../SwiftExport.Contracts/DTOs/Procurement/PurchaseOrders/Query/ProcurementOrderListItemDto.cs)。

### ProcurementOrderReversalController

源码：[ProcurementOrderReversalController](../SwiftExport.WebApi/Controllers/Procurement/PurchaseOrders/Reversal/ProcurementOrderReversalController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| POST `/api/procurement/orders/{uid}/cancel-confirmation-preview` | `Guid uid, [FromBody] ProcurementReversalPreviewRequest request, CancellationToken cancellationToken = default` | `Task<ServiceResult<ProcurementReversalPreviewDto>>` | `Procurement.Order.Confirm` |
| POST `/api/procurement/orders/{uid}/cancel-confirmation` | `Guid uid, [FromBody] CancelProcurementOrderConfirmationCommand request, CancellationToken cancellationToken = default` | `Task<ServiceResult<ProcurementOrderCommandResult>>` | `Procurement.Order.Confirm` |
| POST `/api/procurement/orders/{uid}/void-preview` | `Guid uid, [FromBody] ProcurementReversalPreviewRequest request, CancellationToken cancellationToken = default` | `Task<ServiceResult<ProcurementReversalPreviewDto>>` | `Procurement.Order.Void` |
| POST `/api/procurement/orders/{uid}/void` | `Guid uid, [FromBody] VoidProcurementOrderCommand request, CancellationToken cancellationToken = default` | `Task<ServiceResult<ProcurementOrderCommandResult>>` | `Procurement.Order.Void` |
| POST `/api/procurement/orders/{uid}/delete-lines-preview` | `Guid uid, [FromBody] ProcurementReversalPreviewRequest request, CancellationToken cancellationToken = default` | `Task<ServiceResult<ProcurementReversalPreviewDto>>` | `Procurement.Order.Edit` |
| POST `/api/procurement/orders/{uid}/delete-lines` | `Guid uid, [FromBody] DeleteProcurementDraftLinesCommand request, CancellationToken cancellationToken = default` | `Task<ServiceResult<ProcurementOrderCommandResult>>` | `Procurement.Order.Edit` |

契约源码：[ProcurementOrderCommandResult](../SwiftExport.Contracts/DTOs/Procurement/PurchaseOrders/ProcurementOrderCommandResult.cs)、[VoidProcurementOrderCommand](../SwiftExport.Contracts/DTOs/Procurement/PurchaseOrders/Reversal/VoidProcurementOrderCommand.cs)、[ProcurementReversalPreviewRequest](../SwiftExport.Contracts/DTOs/Procurement/PurchaseOrders/Reversal/ProcurementReversalPreviewRequest.cs)、[ProcurementReversalPreviewDto](../SwiftExport.Contracts/DTOs/Procurement/PurchaseOrders/Reversal/ProcurementReversalPreviewDto.cs)、[DeleteProcurementDraftLinesCommand](../SwiftExport.Contracts/DTOs/Procurement/PurchaseOrders/Reversal/DeleteProcurementDraftLinesCommand.cs)、[CancelProcurementOrderConfirmationCommand](../SwiftExport.Contracts/DTOs/Procurement/PurchaseOrders/Reversal/CancelProcurementOrderConfirmationCommand.cs)。

### ProductCategoryController

源码：[ProductCategoryController](../SwiftExport.WebApi/Controllers/Products/Categories/ProductCategoryController.cs)；继承[BaseController](../SwiftExport.WebApi/Controllers/Base/BaseController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| POST `/api/ProductCategory/page` | `[FromBody] ProductCategorySearchRequest request` | `Task<ServiceResult<PagedResponseBase<ProductCategoryDto>>>` | `Products.Category.View` |
| POST `/api/ProductCategory/batch-save` | `[FromBody] ProductCategoryBatchSaveRequest request` | `Task<ServiceResult<ProductCategoryBatchSaveResponse>>` | `Products.Category.{Create/Edit/Delete}` |
| GET `/api/ProductCategory/all` | `无` | `Task<ServiceResult<IReadOnlyList<ProductCategoryDto>>>` | `Products.Category.View` |
| GET `/api/ProductCategory/{id}` | `Guid id` | `Task<ServiceResult<ProductCategoryDto>>` | `Products.Category.View` |
| POST `/api/ProductCategory` | `[FromBody] CreateItemRequest<ProductCategoryDto> dto` | `Task<ServiceResult<SaveItemResult>>` | `Products.Category.Create` |
| PUT `/api/ProductCategory/{id}` | `Guid id, [FromBody] UpdateItemRequest<ProductCategoryDto> dto` | `Task<ServiceResult<SaveItemResult>>` | `Products.Category.Edit` |
| DELETE `/api/ProductCategory/{id}` | `DeleteItemRequest id` | `Task<ServiceResult<SaveItemResult>>` | `Products.Category.Delete` |

契约源码：[ProductCategoryBatchSaveResponse](../SwiftExport.Contracts/DTOs/Products/Categories/Responses/ProductCategoryBatchSaveResponse.cs)、[ProductCategorySearchRequest](../SwiftExport.Contracts/DTOs/Products/Categories/Requests/ProductCategorySearchRequest.cs)、[ProductCategoryBatchSaveRequest](../SwiftExport.Contracts/DTOs/Products/Categories/Requests/ProductCategoryBatchSaveRequest.cs)、[ProductCategoryDto](../SwiftExport.Contracts/DTOs/Products/Categories/ProductCategoryDto.cs)。

### ProductColorController

源码：[ProductColorController](../SwiftExport.WebApi/Controllers/Products/Colors/ProductColorController.cs)；继承[BaseController](../SwiftExport.WebApi/Controllers/Base/BaseController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| POST `/api/ProductColor/batch-save` | `[FromBody] ProductColorBatchSaveRequest request` | `Task<ServiceResult<ProductColorBatchSaveResponse>>` | `Products.Color.{Create/Edit/Delete}` |
| POST `/api/ProductColor/page` | `[FromBody] ProductColorSearchRequest request` | `Task<ServiceResult<PagedResponseBase<ProductColorDto>>>` | `Products.Color.View` |
| GET `/api/ProductColor/all` | `无` | `Task<ServiceResult<IReadOnlyList<ProductColorDto>>>` | `Products.Color.View` |
| GET `/api/ProductColor/{id}` | `Guid id` | `Task<ServiceResult<ProductColorDto>>` | `Products.Color.View` |
| POST `/api/ProductColor` | `[FromBody] CreateItemRequest<ProductColorDto> dto` | `Task<ServiceResult<SaveItemResult>>` | `Products.Color.Create` |
| PUT `/api/ProductColor/{id}` | `Guid id, [FromBody] UpdateItemRequest<ProductColorDto> dto` | `Task<ServiceResult<SaveItemResult>>` | `Products.Color.Edit` |
| DELETE `/api/ProductColor/{id}` | `DeleteItemRequest id` | `Task<ServiceResult<SaveItemResult>>` | `Products.Color.Delete` |

契约源码：[ProductColorDto](../SwiftExport.Contracts/DTOs/Products/Colors/ProductColorDto.cs)、[ProductColorBatchSaveResponse](../SwiftExport.Contracts/DTOs/Products/Colors/Responses/ProductColorBatchSaveResponse.cs)、[ProductColorSearchRequest](../SwiftExport.Contracts/DTOs/Products/Colors/Requests/ProductColorSearchRequest.cs)、[ProductColorBatchSaveRequest](../SwiftExport.Contracts/DTOs/Products/Colors/Requests/ProductColorBatchSaveRequest.cs)。

### ProductController

源码：[ProductController](../SwiftExport.WebApi/Controllers/Products/Catalog/ProductController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| GET `/api/Product/{id}` | `Guid id` | `Task<ServiceResult<ProductDto>>` | `Products.Product.View` |
| POST `/api/Product` | `[FromBody] CreateItemRequest<ProductDto> dto` | `Task<ServiceResult<SaveItemResult>>` | `Products.Product.Create` |
| PUT `/api/Product/{id}` | `Guid id, [FromBody] UpdateItemRequest<ProductDto> dto` | `Task<ServiceResult<SaveItemResult>>` | `Products.Product.Edit` |
| DELETE `/api/Product/{id}` | `DeleteItemRequest id` | `Task<ServiceResult<SaveItemResult>>` | `Products.Product.Delete` |
| POST `/api/Product/page` | `[FromBody] ProductSearchRequest request` | `Task<ServiceResult<PagedResponseBase<ProductDto>>>` | `Products.Product.View` |
| POST `/api/Product/lookup` | `[FromBody] ProductLookupRequest request` | `Task<ServiceResult<List<ProductLookupDto>>>` | `Products.Product.View` |
| GET `/api/Product/all` | `无` | `Task<ServiceResult<IReadOnlyList<ProductDto>>>` | `Products.Product.View` |
| GET `/api/Product/customerCodes` | `无` | `Task<ServiceResult<IReadOnlyList<string>>>` | `Products.Product.View` |
| GET `/api/Product/sku/{sku}` | `string sku` | `Task<ServiceResult<ProductDto>>` | `Products.Product.View` |
| POST `/api/Product/batch-save` | `[FromBody] ProductBatchSaveRequest request` | `Task<ServiceResult<ProductBatchSaveResponse>>` | `Products.Product.{Create/Edit/Delete}` |
| POST `/api/Product/pull-master-descriptions` | `[FromBody] PullProductMasterDataRequest request, CancellationToken cancellationToken` | `Task<ServiceResult<PullProductMasterDataResponse>>` | `Products.Product.Edit` |
| POST `/api/Product/pull-master-features` | `[FromBody] PullProductMasterDataRequest request, CancellationToken cancellationToken` | `Task<ServiceResult<PullProductMasterDataResponse>>` | `Products.Product.Edit` |
| POST `/api/Product/pull-master-supplier` | `[FromBody] PullProductMasterDataRequest request, CancellationToken cancellationToken` | `Task<ServiceResult<PullProductMasterDataResponse>>` | `Products.Product.Edit` |
| POST `/api/Product/pull-master-weight-volume` | `[FromBody] PullProductMasterDataRequest request, CancellationToken cancellationToken` | `Task<ServiceResult<PullProductMasterDataResponse>>` | `Products.Product.Edit` |
| POST `/api/Product/pull-master-all` | `[FromBody] PullProductMasterDataRequest request, CancellationToken cancellationToken` | `Task<ServiceResult<PullProductMasterDataResponse>>` | `Products.Product.Edit` |
| GET `/api/Product/default-warehouse-candidates` | ` [FromQuery] PagedRequestBase request, CancellationToken cancellationToken` | `Task<ServiceResult<PagedResponseBase<ProductDefaultWarehouseCandidateDto>>>` | `Products.Product.View` |

契约源码：[PullProductMasterDataResponse](../SwiftExport.Contracts/DTOs/Products/Catalog/Responses/PullProductMasterDataResponse.cs)、[ProductBatchSaveResponse](../SwiftExport.Contracts/DTOs/Products/Catalog/Responses/ProductBatchSaveResponse.cs)、[PullProductMasterDataRequest](../SwiftExport.Contracts/DTOs/Products/Catalog/Requests/PullProductMasterDataRequest.cs)、[ProductSearchRequest](../SwiftExport.Contracts/DTOs/Products/Catalog/Requests/ProductSearchRequest.cs)、[ProductLookupRequest](../SwiftExport.Contracts/DTOs/Products/Catalog/Requests/ProductLookupRequest.cs)、[ProductBatchSaveRequest](../SwiftExport.Contracts/DTOs/Products/Catalog/Requests/ProductBatchSaveRequest.cs)、[ProductLookupDto](../SwiftExport.Contracts/DTOs/Products/Catalog/ProductLookupDto.cs)、[ProductDto](../SwiftExport.Contracts/DTOs/Products/Catalog/ProductDto.cs)、[ProductDefaultWarehouseCandidateDto](../SwiftExport.Contracts/DTOs/Products/Catalog/ProductDefaultWarehouseCandidateDto.cs)。

### ProductImageController

源码：[ProductImageController](../SwiftExport.WebApi/Controllers/Files/ProductImageController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| POST `/api/product-images/batch` | ` CancellationToken cancellationToken` | `Task<ActionResult<ServiceResult<ProductImageBatchUploadResultDto>>>` | `System.Files.Manage` |

契约源码：[ProductImageBatchUploadResultDto](../SwiftExport.Contracts/DTOs/Files/ProductImages/ProductImageBatchUploadResultDto.cs)。

### ProductMasterController

源码：[ProductMasterController](../SwiftExport.WebApi/Controllers/Products/ProductMasters/ProductMasterController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| POST `/api/ProductMaster/page` | ` [FromBody] ProductMasterSearchRequest request, CancellationToken cancellationToken` | `Task<ServiceResult<PagedResponseBase<ProductMasterListItemDto>>>` | `Products.Master.View` |
| POST `/api/ProductMaster/assign-category` | ` [FromBody] AssignProductMastersToCategoryRequest request, CancellationToken cancellationToken` | `Task<ServiceResult<AssignProductMastersToCategoryResult>>` | `Products.Master.Edit` |
| GET `/api/ProductMaster/{productMasterId:guid}` | ` Guid productMasterId, CancellationToken cancellationToken` | `Task<ServiceResult<ProductMasterDetailDto>>` | `Products.Master.View` |
| POST `/api/ProductMaster/batch-save` | ` [FromBody] ProductMasterBatchSaveRequest request, CancellationToken cancellationToken` | `Task<ServiceResult<ProductMasterBatchSaveResponse>>` | `Products.Master.{Create/Edit/Delete}` |
| POST `/api/ProductMaster/save-detail` | ` [FromBody] SaveProductMasterDetailRequest request, CancellationToken cancellationToken` | `Task<ServiceResult<SaveProductMasterDetailResponse>>` | `Products.Master.Edit` |
| POST `/api/ProductMaster/sync-descriptions` | `[FromBody] SyncProductMasterDataRequest request, CancellationToken cancellationToken` | `Task<ServiceResult<SyncProductMasterDataResponse>>` | `Products.Product.Edit` |
| POST `/api/ProductMaster/sync-features` | `[FromBody] SyncProductMasterDataRequest request, CancellationToken cancellationToken` | `Task<ServiceResult<SyncProductMasterDataResponse>>` | `Products.Product.Edit` |
| POST `/api/ProductMaster/sync-supplier` | `[FromBody] SyncProductMasterDataRequest request, CancellationToken cancellationToken` | `Task<ServiceResult<SyncProductMasterDataResponse>>` | `Products.Product.Edit` |
| POST `/api/ProductMaster/sync-weight-volume` | `[FromBody] SyncProductMasterDataRequest request, CancellationToken cancellationToken` | `Task<ServiceResult<SyncProductMasterDataResponse>>` | `Products.Product.Edit` |
| POST `/api/ProductMaster/sync-all` | `[FromBody] SyncProductMasterDataRequest request, CancellationToken cancellationToken` | `Task<ServiceResult<SyncProductMasterDataResponse>>` | `Products.Product.Edit` |

契约源码：[ProductMasterListItemDto](../SwiftExport.Contracts/DTOs/Products/ProductMasters/ProductMasterListItemDto.cs)、[ProductMasterDetailDto](../SwiftExport.Contracts/DTOs/Products/ProductMasters/ProductMasterDetailDto.cs)、[AssignProductMastersToCategoryResult](../SwiftExport.Contracts/DTOs/Products/ProductMasters/Responses/AssignProductMastersToCategoryResult.cs)、[SyncProductMasterDataResponse](../SwiftExport.Contracts/DTOs/Products/ProductMasters/Responses/SyncProductMasterDataResponse.cs)、[SaveProductMasterDetailResponse](../SwiftExport.Contracts/DTOs/Products/ProductMasters/Responses/SaveProductMasterDetailResponse.cs)、[ProductMasterBatchSaveResponse](../SwiftExport.Contracts/DTOs/Products/ProductMasters/Responses/ProductMasterBatchSaveResponse.cs)、[SyncProductMasterDataRequest](../SwiftExport.Contracts/DTOs/Products/ProductMasters/Requests/SyncProductMasterDataRequest.cs)、[SaveProductMasterDetailRequest](../SwiftExport.Contracts/DTOs/Products/ProductMasters/Requests/SaveProductMasterDetailRequest.cs)、[ProductMasterSearchRequest](../SwiftExport.Contracts/DTOs/Products/ProductMasters/Requests/ProductMasterSearchRequest.cs)、[ProductMasterBatchSaveRequest](../SwiftExport.Contracts/DTOs/Products/ProductMasters/Requests/ProductMasterBatchSaveRequest.cs)、[AssignProductMastersToCategoryRequest](../SwiftExport.Contracts/DTOs/Products/ProductMasters/Requests/AssignProductMastersToCategoryRequest.cs)。

### ProductNeutralController

源码：[ProductNeutralController](../SwiftExport.WebApi/Controllers/Products/ProductNeutrals/ProductNeutralController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| POST `/api/ProductNeutral/{productMasterId:guid}/batch-save` | ` Guid productMasterId, [FromBody] BatchSaveRequestBase<ProductNeutralWriteDto> request, CancellationToken cancellationToken` | `Task<ServiceResult<ProductMasterDetailDto>>` | `Products.Neutral.{Create/Edit/Delete}` |

契约源码：[ProductMasterDetailDto](../SwiftExport.Contracts/DTOs/Products/ProductMasters/ProductMasterDetailDto.cs)、[ProductNeutralWriteDto](../SwiftExport.Contracts/DTOs/Products/ProductMasters/ProductNeutralWriteDto.cs)。

### ProductNeutralManagerController

源码：[ProductNeutralManagerController](../SwiftExport.WebApi/Controllers/Products/ProductNeutrals/ProductNeutralManagerController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| POST `/api/ProductNeutralManager/page` | ` [FromBody] ProductNeutralManagerSearchRequest request, CancellationToken cancellationToken` | `Task<ServiceResult<PagedResponseBase<ProductNeutralManagerListItemDto>>>` | `Products.Neutral.View` |
| POST `/api/ProductNeutralManager/batch-save` | ` [FromBody] ProductNeutralManagerBatchSaveRequest request, CancellationToken cancellationToken` | `Task<ServiceResult<ProductNeutralManagerBatchSaveResponse>>` | `Products.Neutral.{Create/Edit/Delete}` |

契约源码：[ProductNeutralManagerBatchSaveResponse](../SwiftExport.Contracts/DTOs/Products/ProductNeutrals/Responses/ProductNeutralManagerBatchSaveResponse.cs)、[ProductNeutralManagerSearchRequest](../SwiftExport.Contracts/DTOs/Products/ProductNeutrals/Requests/ProductNeutralManagerSearchRequest.cs)、[ProductNeutralManagerBatchSaveRequest](../SwiftExport.Contracts/DTOs/Products/ProductNeutrals/Requests/ProductNeutralManagerBatchSaveRequest.cs)、[ProductNeutralManagerListItemDto](../SwiftExport.Contracts/DTOs/Products/ProductNeutrals/ProductNeutralManagerListItemDto.cs)。

### PurchaseDraftCreationController

源码：[PurchaseDraftCreationController](../SwiftExport.WebApi/Controllers/Procurement/PurchaseOrders/Draft/PurchaseDraftCreationController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| POST `/api/procurement/purchase-drafts/create` | `[FromBody] CreatePurchaseDraftsCommand command, CancellationToken cancellationToken` | `Task<ServiceResult<CreatePurchaseDraftsResult>>` | `Procurement.Order.Create` |

契约源码：[CreatePurchaseDraftsResult](../SwiftExport.Contracts/DTOs/Procurement/PurchaseOrders/Sources/CreatePurchaseDraftsResult.cs)、[CreatePurchaseDraftsCommand](../SwiftExport.Contracts/DTOs/Procurement/PurchaseOrders/Sources/CreatePurchaseDraftsCommand.cs)。

### PurchaseDraftPreviewController

源码：[PurchaseDraftPreviewController](../SwiftExport.WebApi/Controllers/Procurement/PurchaseOrders/Draft/PurchaseDraftPreviewController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| POST `/api/procurement/purchase-drafts/preview` | `[FromBody] PurchaseDraftPreviewRequest request, CancellationToken cancellationToken` | `Task<ServiceResult<PurchaseDraftPreviewResponse>>` | `Procurement.Order.Create` |

契约源码：[PurchaseDraftPreviewResponse](../SwiftExport.Contracts/DTOs/Procurement/PurchaseOrders/Sources/PurchaseDraftPreviewResponse.cs)、[PurchaseDraftPreviewRequest](../SwiftExport.Contracts/DTOs/Procurement/PurchaseOrders/Sources/PurchaseDraftPreviewRequest.cs)。

### PurchaseDraftSourceController

源码：[PurchaseDraftSourceController](../SwiftExport.WebApi/Controllers/Procurement/PurchaseOrders/Sources/PurchaseDraftSourceController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| GET `/api/procurement/purchase-drafts/candidates/source-groups` | `CancellationToken cancellationToken` | `Task<ServiceResult<List<PurchaseDraftCandidateSourceGroupDto>>>` | `Procurement.Order.Read` |
| POST `/api/procurement/purchase-drafts/candidates` | `[FromBody] PurchaseDraftCandidateQuery request, CancellationToken cancellationToken` | `Task<ServiceResult<List<PurchaseDraftCandidateDto>>>` | `Procurement.Order.Read` |

契约源码：[PurchaseDraftCandidateSourceGroupDto](../SwiftExport.Contracts/DTOs/Procurement/PurchaseOrders/Sources/PurchaseDraftCandidateSourceGroupDto.cs)、[PurchaseDraftCandidateQuery](../SwiftExport.Contracts/DTOs/Procurement/PurchaseOrders/Sources/PurchaseDraftCandidateQuery.cs)、[PurchaseDraftCandidateDto](../SwiftExport.Contracts/DTOs/Procurement/PurchaseOrders/Sources/PurchaseDraftCandidateDto.cs)。

### PurchaseOrderManagerQueryController

源码：[PurchaseOrderManagerQueryController](../SwiftExport.WebApi/Controllers/Procurement/PurchaseOrders/Query/PurchaseOrderManagerQueryController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| POST `/api/procurement/order-manager/search` | ` [FromBody] PurchaseOrderManagerSearchRequest request, CancellationToken cancellationToken = default` | `Task<ServiceResult<PagedResponseBase<PurchaseOrderManagerListItemDto>>>` | `Procurement.Order.Read` |
| GET `/api/procurement/order-manager/{purchaseOrderId:guid}/lines` | ` Guid purchaseOrderId, CancellationToken cancellationToken = default` | `Task<ServiceResult<IReadOnlyList<PurchaseOrderManagerLineDto>>>` | `Procurement.Order.Read` |

契约源码：[PurchaseOrderManagerSearchRequest](../SwiftExport.Contracts/DTOs/Procurement/PurchaseOrders/Query/PurchaseOrderManagerSearchRequest.cs)、[PurchaseOrderManagerListItemDto](../SwiftExport.Contracts/DTOs/Procurement/PurchaseOrders/Query/PurchaseOrderManagerListItemDto.cs)、[PurchaseOrderManagerLineDto](../SwiftExport.Contracts/DTOs/Procurement/PurchaseOrders/Query/PurchaseOrderManagerLineDto.cs)。

### PurchaseOrderProgressQueryController

源码：[PurchaseOrderProgressQueryController](../SwiftExport.WebApi/Controllers/Procurement/PurchaseOrders/Progress/PurchaseOrderProgressQueryController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| POST `/api/procurement/purchase-order-progress/search` | ` [FromBody] PurchaseOrderProgressSearchRequest request, CancellationToken cancellationToken` | `Task<ServiceResult<PagedResponseBase<PurchaseOrderProgressListItemDto>>>` | `Procurement.Progress.View` |
| GET `/api/procurement/purchase-order-progress/{purchaseOrderId:guid}/detail` | ` Guid purchaseOrderId, CancellationToken cancellationToken` | `Task<ServiceResult<PurchaseOrderProgressDetailDto>>` | `Procurement.Progress.View` |

契约源码：[PurchaseOrderProgressSearchRequest](../SwiftExport.Contracts/DTOs/Procurement/PurchaseOrders/Progress/PurchaseOrderProgressSearchRequest.cs)、[PurchaseOrderProgressListItemDto](../SwiftExport.Contracts/DTOs/Procurement/PurchaseOrders/Progress/PurchaseOrderProgressListItemDto.cs)、[PurchaseOrderProgressDetailDto](../SwiftExport.Contracts/DTOs/Procurement/PurchaseOrders/Progress/PurchaseOrderProgressDetailDto.cs)。

### PurchaseOrderProgressReportController

源码：[PurchaseOrderProgressReportController](../SwiftExport.WebApi/Controllers/Procurement/PurchaseOrders/Progress/PurchaseOrderProgressReportController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| POST `/api/procurement/purchase-order-progress/{purchaseOrderId:guid}/reports` | ` Guid purchaseOrderId, [FromBody] ReportPurchaseOrderProgressCommand command, CancellationToken cancellationToken` | `Task<ServiceResult<PurchaseOrderProgressReportResult>>` | `Procurement.Progress.Report` |

契约源码：[ReportPurchaseOrderProgressCommand](../SwiftExport.Contracts/DTOs/Procurement/PurchaseOrders/Progress/ReportPurchaseOrderProgressCommand.cs)、[PurchaseOrderProgressReportResult](../SwiftExport.Contracts/DTOs/Procurement/PurchaseOrders/Progress/PurchaseOrderProgressReportResult.cs)。

### PurchaseOrderTraceController

源码：[PurchaseOrderTraceController](../SwiftExport.WebApi/Controllers/Procurement/PurchaseOrders/Trace/PurchaseOrderTraceController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| GET `/api/procurement/purchase-order-trace/{purchaseOrderId:guid}` | `Guid purchaseOrderId, CancellationToken cancellationToken = default` | `Task<ServiceResult<PurchaseOrderTraceDto>>` | `Procurement.Order.Read` |

契约源码：[PurchaseOrderTraceDto](../SwiftExport.Contracts/DTOs/Procurement/PurchaseOrders/Trace/PurchaseOrderTraceDto.cs)。

### RbacPermissionsController

源码：[RbacPermissionsController](../SwiftExport.WebApi/Controllers/Systems/RbacPermissionsController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| GET `/api/system/permissions` | `CancellationToken cancellationToken` | `Task<ServiceResult<List<ManagedPermissionDto>>>` | `服务内：System.Permissions.View` |

契约源码：[RbacManagementDtos](../SwiftExport.Contracts/DTOs/Auth/Permissions/RbacManagementDtos.cs)。

### RbacRolesController

源码：[RbacRolesController](../SwiftExport.WebApi/Controllers/Systems/RbacRolesController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| POST `/api/system/roles/page` | `[FromBody] ManagedPageRequest request, CancellationToken cancellationToken` | `Task<ServiceResult<PagedResponseBase<ManagedRoleDto>>>` | `服务内：System.Roles.View；写需 SYSTEM_ADMIN` |
| GET `/api/system/roles/{id:guid}` | `Guid id, CancellationToken cancellationToken` | `Task<ServiceResult<ManagedRoleDto>>` | `服务内：System.Roles.View；写需 SYSTEM_ADMIN` |
| POST `/api/system/roles` | `[FromBody] CreateManagedRoleRequest request, CancellationToken cancellationToken` | `Task<ServiceResult<ManagedRoleDto>>` | `服务内：System.Roles.View；写需 SYSTEM_ADMIN` |
| PUT `/api/system/roles/{id:guid}` | `Guid id, [FromBody] UpdateManagedRoleRequest request, CancellationToken cancellationToken` | `Task<ServiceResult<ManagedRoleDto>>` | `服务内：System.Roles.View；写需 SYSTEM_ADMIN` |
| PUT `/api/system/roles/{id:guid}/status` | `Guid id, [FromBody] SetManagedStatusRequest request, CancellationToken cancellationToken` | `Task<ServiceResult<ManagedRoleDto>>` | `服务内：System.Roles.View；写需 SYSTEM_ADMIN` |
| DELETE `/api/system/roles/{id:guid}` | `Guid id, [FromQuery] int expectedVersion, CancellationToken cancellationToken` | `Task<ServiceResult<bool>>` | `服务内：System.Roles.View；写需 SYSTEM_ADMIN` |
| GET `/api/system/roles/{id:guid}/permissions` | `Guid id, CancellationToken cancellationToken` | `Task<ServiceResult<RolePermissionsDto>>` | `服务内：System.Roles.View；写需 SYSTEM_ADMIN` |
| PUT `/api/system/roles/{id:guid}/permissions` | `Guid id, [FromBody] ReplaceRolePermissionsRequest request, CancellationToken cancellationToken` | `Task<ServiceResult<RolePermissionsDto>>` | `服务内：System.Roles.View；写需 SYSTEM_ADMIN` |

契约源码：[RbacManagementDtos](../SwiftExport.Contracts/DTOs/Auth/Permissions/RbacManagementDtos.cs)。

### RbacUsersController

源码：[RbacUsersController](../SwiftExport.WebApi/Controllers/Systems/RbacUsersController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| POST `/api/system/users/page` | `[FromBody] ManagedPageRequest request, CancellationToken cancellationToken` | `Task<ServiceResult<PagedResponseBase<ManagedUserDto>>>` | `服务内：System.Users.View；写需 SYSTEM_ADMIN` |
| GET `/api/system/users/{id:guid}` | `Guid id, CancellationToken cancellationToken` | `Task<ServiceResult<ManagedUserDto>>` | `服务内：System.Users.View；写需 SYSTEM_ADMIN` |
| PUT `/api/system/users/{id:guid}` | `Guid id, [FromBody] UpdateManagedUserRequest request, CancellationToken cancellationToken` | `Task<ServiceResult<ManagedUserDto>>` | `服务内：System.Users.View；写需 SYSTEM_ADMIN` |
| PUT `/api/system/users/{id:guid}/status` | `Guid id, [FromBody] SetManagedStatusRequest request, CancellationToken cancellationToken` | `Task<ServiceResult<ManagedUserDto>>` | `服务内：System.Users.View；写需 SYSTEM_ADMIN` |
| GET `/api/system/users/{id:guid}/roles` | `Guid id, CancellationToken cancellationToken` | `Task<ServiceResult<UserRolesDto>>` | `服务内：System.Users.View；写需 SYSTEM_ADMIN` |
| PUT `/api/system/users/{id:guid}/roles` | `Guid id, [FromBody] ReplaceUserRolesRequest request, CancellationToken cancellationToken` | `Task<ServiceResult<UserRolesDto>>` | `服务内：System.Users.View；写需 SYSTEM_ADMIN` |
| GET `/api/system/users/{id:guid}/effective-permissions` | `Guid id, CancellationToken cancellationToken` | `Task<ServiceResult<List<string>>>` | `服务内：System.Users.View；写需 SYSTEM_ADMIN` |

契约源码：[RbacManagementDtos](../SwiftExport.Contracts/DTOs/Auth/Permissions/RbacManagementDtos.cs)。

### SalesFulfillmentQueryController

源码：[SalesFulfillmentQueryController](../SwiftExport.WebApi/Controllers/SalesOrders/Query/SalesFulfillmentQueryController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| GET `/api/sales-orders/fulfillment/{uid:guid}` | `[FromRoute] Guid uid, CancellationToken cancellationToken` | `Task<ServiceResult<SalesFulfillmentDetailDto>>` | `Sales.Order.View` |

契约源码：[SalesFulfillmentDetailDto](../SwiftExport.Contracts/DTOs/SalesOrders/Fulfillment/SalesFulfillmentDetailDto.cs)。

### SalesOrderConfirmationController

源码：[SalesOrderConfirmationController](../SwiftExport.WebApi/Controllers/SalesOrders/Confirmation/SalesOrderConfirmationController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| POST `/api/sales-orders/detail/{uid:guid}/confirm` | `[FromRoute] Guid uid, [FromBody] ConfirmSalesOrderCommand request` | `Task<ServiceResult<SalesOrderConfirmationResultDto>>` | `Sales.Order.Confirm` |
| POST `/api/sales-orders/detail/{uid:guid}/cancel-confirmation` | `[FromRoute] Guid uid, [FromBody] CancelSalesOrderConfirmationCommand request` | `Task<ServiceResult<SalesOrderConfirmationResultDto>>` | `Sales.Order.CancelConfirmation` |

契约源码：[SalesOrderConfirmationResultDto](../SwiftExport.Contracts/DTOs/SalesOrders/Confirmation/SalesOrderConfirmationResultDto.cs)、[ConfirmSalesOrderCommand](../SwiftExport.Contracts/DTOs/SalesOrders/Confirmation/ConfirmSalesOrderCommand.cs)、[CancelSalesOrderConfirmationCommand](../SwiftExport.Contracts/DTOs/SalesOrders/Confirmation/CancelSalesOrderConfirmationCommand.cs)。

### SalesOrderDraftController

源码：[SalesOrderDraftController](../SwiftExport.WebApi/Controllers/SalesOrders/Draft/SalesOrderDraftController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| POST `/api/sales-orders/detail/save` | `[FromBody] SaveSalesOrderRequest request` | `Task<ServiceResult<SaveSalesOrderResponse>>` | `Sales.Order.Maintain` |
| POST `/api/sales-orders/detail/{uid:guid}/delete-draft` | `[FromRoute] Guid uid, [FromBody] SalesOrderCommandRequest request` | `Task<ServiceResult<bool>>` | `Sales.Order.Delete` |

契约源码：[SaveSalesOrderResponse](../SwiftExport.Contracts/DTOs/SalesOrders/Draft/SaveSalesOrderResponse.cs)、[SaveSalesOrderRequest](../SwiftExport.Contracts/DTOs/SalesOrders/Draft/SaveSalesOrderRequest.cs)、[SalesOrderCommandRequest](../SwiftExport.Contracts/DTOs/SalesOrders/Draft/SalesOrderCommandRequest.cs)。

### SalesOrderLifecycleController

源码：[SalesOrderLifecycleController](../SwiftExport.WebApi/Controllers/SalesOrders/Lifecycle/SalesOrderLifecycleController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| POST `/api/sales-orders/detail/{uid:guid}/close` | `[FromRoute] Guid uid, [FromBody] CloseSalesOrderRequest request` | `Task<ServiceResult<SalesOrderDetailDto>>` | `Sales.Order.Lifecycle` |

契约源码：[SalesOrderDetailDto](../SwiftExport.Contracts/DTOs/SalesOrders/Query/SalesOrderDetailDto.cs)、[CloseSalesOrderRequest](../SwiftExport.Contracts/DTOs/SalesOrders/Lifecycle/CloseSalesOrderRequest.cs)。

### SalesOrderQueryController

源码：[SalesOrderQueryController](../SwiftExport.WebApi/Controllers/SalesOrders/Query/SalesOrderQueryController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| POST `/api/sales-orders/manager/search` | `[FromBody] SalesOrderManagerSearchRequest request, CancellationToken cancellationToken` | `Task<ServiceResult<PagedResponseBase<SalesOrderManagerListItemDto>>>` | `Sales.Order.View` |
| GET `/api/sales-orders/manager/customer-codes` | `CancellationToken cancellationToken` | `Task<ServiceResult<List<string>>>` | `Sales.Order.View` |
| GET `/api/sales-orders/detail/{uid:guid}` | `[FromRoute] Guid uid, CancellationToken cancellationToken` | `Task<ServiceResult<SalesOrderDetailDto>>` | `Sales.Order.View` |
| POST `/api/sales-orders/detail/products/search` | `[FromBody] SalesOrderProductSearchRequest request, CancellationToken cancellationToken` | `Task<ServiceResult<PagedResponseBase<SalesOrderProductLookupDto>>>` | `Sales.Order.View` |
| POST `/api/sales-orders/detail/customers/search` | `[FromBody] SalesOrderCustomerSearchRequest request, CancellationToken cancellationToken` | `Task<ServiceResult<PagedResponseBase<SalesOrderCustomerLookupDto>>>` | `Sales.Order.View` |

契约源码：[SalesOrderProductSearchRequest](../SwiftExport.Contracts/DTOs/SalesOrders/Query/SalesOrderProductSearchRequest.cs)、[SalesOrderProductLookupDto](../SwiftExport.Contracts/DTOs/SalesOrders/Query/SalesOrderProductLookupDto.cs)、[SalesOrderManagerSearchRequest](../SwiftExport.Contracts/DTOs/SalesOrders/Query/SalesOrderManagerSearchRequest.cs)、[SalesOrderManagerListItemDto](../SwiftExport.Contracts/DTOs/SalesOrders/Query/SalesOrderManagerListItemDto.cs)、[SalesOrderDetailDto](../SwiftExport.Contracts/DTOs/SalesOrders/Query/SalesOrderDetailDto.cs)、[SalesOrderCustomerSearchRequest](../SwiftExport.Contracts/DTOs/SalesOrders/Query/SalesOrderCustomerSearchRequest.cs)、[SalesOrderCustomerLookupDto](../SwiftExport.Contracts/DTOs/SalesOrders/Query/SalesOrderCustomerLookupDto.cs)。

### SourceDemandDeliveryController

源码：[SourceDemandDeliveryController](../SwiftExport.WebApi/Controllers/DemandPlanning/Sources/Delivery/SourceDemandDeliveryController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| POST `/api/demand-planning/source-demands/delivery/search` | ` [FromBody] SourceDemandDeliverySearchRequest request, CancellationToken cancellationToken` | `Task<ActionResult<ServiceResult<PagedResponseBase<SourceDemandDeliveryItemDto>>>>` | `DemandPlanning.Source.View` |
| GET `/api/demand-planning/source-demands/delivery/{grossDemandId:guid}` | ` [FromRoute] Guid grossDemandId, CancellationToken cancellationToken` | `Task<ActionResult<ServiceResult<SourceDemandDeliveryItemDto>>>` | `DemandPlanning.Source.View` |

契约源码：[SourceDemandDeliverySearchRequest](../SwiftExport.Contracts/DTOs/DemandPlanning/Sources/Delivery/SourceDemandDeliverySearchRequest.cs)、[SourceDemandDeliveryItemDto](../SwiftExport.Contracts/DTOs/DemandPlanning/Sources/Delivery/SourceDemandDeliveryItemDto.cs)。

### SupplierController

源码：[SupplierController](../SwiftExport.WebApi/Controllers/Suppliers/Management/SupplierController.cs)；继承[BaseController](../SwiftExport.WebApi/Controllers/Base/BaseController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| POST `/api/Supplier/page` | `[FromBody] SupplierSearchRequest request` | `Task<ServiceResult<PagedResponseBase<SupplierDto>>>` | `Suppliers.Supplier.View` |
| POST `/api/Supplier/batch-save` | `[FromBody] SupplierBatchSaveRequest req` | `Task<ServiceResult<SupplierBatchSaveResponse>>` | `Suppliers.Supplier.{Create/Edit/Delete}` |
| GET `/api/Supplier/all` | `无` | `Task<ServiceResult<IReadOnlyList<SupplierDto>>>` | `Suppliers.Supplier.View` |
| GET `/api/Supplier/{id}` | `Guid id` | `Task<ServiceResult<SupplierDto>>` | `Suppliers.Supplier.View` |
| POST `/api/Supplier` | `[FromBody] CreateItemRequest<SupplierDto> dto` | `Task<ServiceResult<SaveItemResult>>` | `Suppliers.Supplier.Create` |
| PUT `/api/Supplier/{id}` | `Guid id, [FromBody] UpdateItemRequest<SupplierDto> dto` | `Task<ServiceResult<SaveItemResult>>` | `Suppliers.Supplier.Edit` |
| DELETE `/api/Supplier/{id}` | `DeleteItemRequest id` | `Task<ServiceResult<SaveItemResult>>` | `Suppliers.Supplier.Delete` |

契约源码：[SupplierDto](../SwiftExport.Contracts/DTOs/Suppliers/Management/SupplierDto.cs)、[SupplierBatchSaveResponse](../SwiftExport.Contracts/DTOs/Suppliers/Management/Responses/SupplierBatchSaveResponse.cs)、[SupplierSearchRequest](../SwiftExport.Contracts/DTOs/Suppliers/Management/Requests/SupplierSearchRequest.cs)、[SupplierBatchSaveRequest](../SwiftExport.Contracts/DTOs/Suppliers/Management/Requests/SupplierBatchSaveRequest.cs)。

### SupplierProductController

源码：[SupplierProductController](../SwiftExport.WebApi/Controllers/Suppliers/SupplierProducts/SupplierProductController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| GET `/api/SupplierProduct/{id}` | `Guid id` | `Task<ServiceResult<SupplierProductDto>>` | `Suppliers.Products.View` |
| POST `/api/SupplierProduct` | `[FromBody] CreateItemRequest<SupplierProductDto> dto` | `Task<ServiceResult<SaveItemResult>>` | `Suppliers.Products.Create` |
| PUT `/api/SupplierProduct/{id}` | `Guid id, [FromBody] UpdateItemRequest<SupplierProductDto> dto` | `Task<ServiceResult<SaveItemResult>>` | `Suppliers.Products.Edit` |
| DELETE `/api/SupplierProduct/{id}` | `DeleteItemRequest id` | `Task<ServiceResult<SaveItemResult>>` | `Suppliers.Products.Delete` |
| POST `/api/SupplierProduct/page` | `[FromBody] SupplierProductSearchRequest request` | `Task<ServiceResult<PagedResponseBase<SupplierProductDto>>>` | `Suppliers.Products.View` |
| POST `/api/SupplierProduct/batch-save` | `[FromBody] SupplierProductBatchSaveRequest request` | `Task<ServiceResult<SupplierProductBatchSaveResponse>>` | `Suppliers.Products.{Create/Edit/Delete}` |
| POST `/api/SupplierProduct/set-product-default-supplier` | `[FromBody] SetProductDefaultSupplierCommand command` | `Task<ServiceResult<SetProductDefaultSupplierResult>>` | `Suppliers.Products.Edit` |

契约源码：[SupplierProductDto](../SwiftExport.Contracts/DTOs/Suppliers/SupplierProducts/SupplierProductDto.cs)、[SupplierProductBatchSaveResponse](../SwiftExport.Contracts/DTOs/Suppliers/SupplierProducts/Responses/SupplierProductBatchSaveResponse.cs)、[SupplierProductSearchRequest](../SwiftExport.Contracts/DTOs/Suppliers/SupplierProducts/Requests/SupplierProductSearchRequest.cs)、[SupplierProductBatchSaveRequest](../SwiftExport.Contracts/DTOs/Suppliers/SupplierProducts/Requests/SupplierProductBatchSaveRequest.cs)、[SetProductDefaultSupplierResult](../SwiftExport.Contracts/DTOs/Products/Catalog/DefaultSupplier/SetProductDefaultSupplierResult.cs)、[SetProductDefaultSupplierCommand](../SwiftExport.Contracts/DTOs/Products/Catalog/DefaultSupplier/SetProductDefaultSupplierCommand.cs)。

### SysDicCurrencyController

源码：[SysDicCurrencyController](../SwiftExport.WebApi/Controllers/Systems/Currencies/SysDicCurrencyController.cs)；继承[BaseController](../SwiftExport.WebApi/Controllers/Base/BaseController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| GET `/api/SysDicCurrency/all` | `无` | `Task<ServiceResult<IReadOnlyList<SysDicCurrencyDto>>>` | `System.Currency.View` |
| GET `/api/SysDicCurrency/base` | `无` | `Task<ServiceResult<SysDicCurrencyDto>>` | `System.Currency.View` |
| GET `/api/SysDicCurrency/enabled` | `无` | `Task<ServiceResult<IReadOnlyList<SysDicCurrencyDto>>>` | `System.Currency.View` |
| GET `/api/SysDicCurrency/defaultbasecurrency` | `无` | `Task<ServiceResult<SysDicCurrencyDetailDto>>` | `System.Currency.View` |
| POST `/api/SysDicCurrency/setnewbase` | `[FromBody] SysDicCurrencyDto dto` | `Task<ServiceResult<int>>` | `System.Currency.Manage` |
| POST `/api/SysDicCurrency/page` | `[FromBody] SysDicCurrencySearchRequest request` | `Task<ServiceResult<PagedResponseBase<SysDicCurrencyDto>>>` | `System.Currency.View` |
| POST `/api/SysDicCurrency/batch-save` | `[FromBody] SysDicCurrencyBatchSaveRequest req` | `Task<ServiceResult<SysDicCurrencyBatchSaveResponse>>` | `System.Currency.{Create/Edit/Delete}` |
| GET `/api/SysDicCurrency/{id}` | `Guid id` | `Task<ServiceResult<SysDicCurrencyDto>>` | `System.Currency.View` |
| POST `/api/SysDicCurrency` | `[FromBody] CreateItemRequest<SysDicCurrencyDto> dto` | `Task<ServiceResult<SaveItemResult>>` | `System.Currency.Create` |
| PUT `/api/SysDicCurrency/{id}` | `Guid id, [FromBody] UpdateItemRequest<SysDicCurrencyDto> dto` | `Task<ServiceResult<SaveItemResult>>` | `System.Currency.Edit` |
| DELETE `/api/SysDicCurrency/{id}` | `DeleteItemRequest id` | `Task<ServiceResult<SaveItemResult>>` | `System.Currency.Delete` |

契约源码：[SysDicCurrencyDto](../SwiftExport.Contracts/DTOs/SystemDtos/Currencies/SysDicCurrencyDto.cs)、[SysDicCurrencyDetailDto](../SwiftExport.Contracts/DTOs/SystemDtos/Currencies/SysDicCurrencyDetailDto.cs)、[SysDicCurrencyBatchSaveResponse](../SwiftExport.Contracts/DTOs/SystemDtos/Currencies/Responses/SysDicCurrencyBatchSaveResponse.cs)、[SysDicCurrencySearchRequest](../SwiftExport.Contracts/DTOs/SystemDtos/Currencies/Requests/SysDicCurrencySearchRequest.cs)、[SysDicCurrencyBatchSaveRequest](../SwiftExport.Contracts/DTOs/SystemDtos/Currencies/Requests/SysDicCurrencyBatchSaveRequest.cs)。

### SysDicCurrencyExchangeController

源码：[SysDicCurrencyExchangeController](../SwiftExport.WebApi/Controllers/Systems/ExchangeRates/SysDicCurrencyExchangeController.cs)；继承[BaseController](../SwiftExport.WebApi/Controllers/Base/BaseController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| GET `/api/SysDicCurrencyExchange/all` | `无` | `Task<ServiceResult<IReadOnlyList<SysDicCurrencyExchangeDto>>>` | `System.ExchangeRate.View` |
| POST `/api/SysDicCurrencyExchange/page` | `[FromBody] SysDicCurrencyExchangeSearchRequest request` | `Task<ServiceResult<PagedResponseBase<SysDicCurrencyExchangeDto>>>` | `System.ExchangeRate.View` |
| POST `/api/SysDicCurrencyExchange/batch-save` | `[FromBody] SysDicCurrencyExchangeBatchSaveRequest req` | `Task<ServiceResult<SysDicCurrencyExchangeBatchSaveResponse>>` | `System.ExchangeRate.{Create/Edit/Delete}` |
| GET `/api/SysDicCurrencyExchange/{id}` | `Guid id` | `Task<ServiceResult<SysDicCurrencyExchangeDto>>` | `System.ExchangeRate.View` |
| POST `/api/SysDicCurrencyExchange` | `[FromBody] CreateItemRequest<SysDicCurrencyExchangeDto> dto` | `Task<ServiceResult<SaveItemResult>>` | `System.ExchangeRate.Create` |
| PUT `/api/SysDicCurrencyExchange/{id}` | `Guid id, [FromBody] UpdateItemRequest<SysDicCurrencyExchangeDto> dto` | `Task<ServiceResult<SaveItemResult>>` | `System.ExchangeRate.Edit` |
| DELETE `/api/SysDicCurrencyExchange/{id}` | `DeleteItemRequest id` | `Task<ServiceResult<SaveItemResult>>` | `System.ExchangeRate.Delete` |

契约源码：[SysDicCurrencyExchangeDto](../SwiftExport.Contracts/DTOs/SystemDtos/ExchangeRates/SysDicCurrencyExchangeDto.cs)、[SysDicCurrencyExchangeBatchSaveResponse](../SwiftExport.Contracts/DTOs/SystemDtos/ExchangeRates/Responses/SysDicCurrencyExchangeBatchSaveResponse.cs)、[SysDicCurrencyExchangeSearchRequest](../SwiftExport.Contracts/DTOs/SystemDtos/ExchangeRates/Requests/SysDicCurrencyExchangeSearchRequest.cs)、[SysDicCurrencyExchangeBatchSaveRequest](../SwiftExport.Contracts/DTOs/SystemDtos/ExchangeRates/Requests/SysDicCurrencyExchangeBatchSaveRequest.cs)。

### SysDictDataController

源码：[SysDictDataController](../SwiftExport.WebApi/Controllers/Systems/Dictionaries/Data/SysDictDataController.cs)；继承[BaseController](../SwiftExport.WebApi/Controllers/Base/BaseController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| POST `/api/SysDictData/page` | `[FromBody] SysDictDataSearchRequest request` | `Task<ServiceResult<PagedResponseBase<SysDictDataDto>>>` | `System.Dictionary.View` |
| POST `/api/SysDictData/batch-save` | `[FromBody] SysDictDataBatchSaveRequest req` | `Task<ServiceResult<SysDictDataBatchSaveResponse>>` | `System.Dictionary.{Create/Edit/Delete}` |
| GET `/api/SysDictData/all` | `无` | `Task<ServiceResult<IReadOnlyList<SysDictDataDto>>>` | `System.Dictionary.View` |
| GET `/api/SysDictData/by-code/{dictCode}` | `string dictCode` | `Task<ServiceResult<IReadOnlyList<SysDictDataDto>>>` | `System.Dictionary.View` |
| GET `/api/SysDictData/{id}` | `Guid id` | `Task<ServiceResult<SysDictDataDto>>` | `System.Dictionary.View` |
| POST `/api/SysDictData` | `[FromBody] CreateItemRequest<SysDictDataDto> dto` | `Task<ServiceResult<SaveItemResult>>` | `System.Dictionary.Create` |
| PUT `/api/SysDictData/{id}` | `Guid id, [FromBody] UpdateItemRequest<SysDictDataDto> dto` | `Task<ServiceResult<SaveItemResult>>` | `System.Dictionary.Edit` |
| DELETE `/api/SysDictData/{id}` | `DeleteItemRequest id` | `Task<ServiceResult<SaveItemResult>>` | `System.Dictionary.Delete` |

契约源码：[SysDictDataDto](../SwiftExport.Contracts/DTOs/SystemDtos/Dictionaries/Data/SysDictDataDto.cs)、[SysDictDataBatchSaveResponse](../SwiftExport.Contracts/DTOs/SystemDtos/Dictionaries/Data/Responses/SysDictDataBatchSaveResponse.cs)、[SysDictDataSearchRequest](../SwiftExport.Contracts/DTOs/SystemDtos/Dictionaries/Data/Requests/SysDictDataSearchRequest.cs)、[SysDictDataBatchSaveRequest](../SwiftExport.Contracts/DTOs/SystemDtos/Dictionaries/Data/Requests/SysDictDataBatchSaveRequest.cs)。

### SysDictTypeController

源码：[SysDictTypeController](../SwiftExport.WebApi/Controllers/Systems/Dictionaries/Types/SysDictTypeController.cs)；继承[BaseController](../SwiftExport.WebApi/Controllers/Base/BaseController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| GET `/api/SysDictType/all` | `无` | `Task<ServiceResult<IReadOnlyList<SysDictTypeDto>>>` | `System.Dictionary.View` |
| GET `/api/SysDictType/group-tree` | `无` | `Task<ServiceResult<List<DictTypeGroupTreeDto>>>` | `System.Dictionary.View` |
| POST `/api/SysDictType/page` | `[FromBody] SysDictTypeSearchRequest request` | `Task<ServiceResult<PagedResponseBase<SysDictTypeDto>>>` | `System.Dictionary.View` |
| POST `/api/SysDictType/batch-save` | `[FromBody] SysDictTypeBatchSaveRequest req` | `Task<ServiceResult<SysDictTypeBatchSaveResponse>>` | `System.Dictionary.{Create/Edit/Delete}` |
| GET `/api/SysDictType/{id}` | `Guid id` | `Task<ServiceResult<SysDictTypeDto>>` | `System.Dictionary.View` |
| POST `/api/SysDictType` | `[FromBody] CreateItemRequest<SysDictTypeDto> dto` | `Task<ServiceResult<SaveItemResult>>` | `System.Dictionary.Create` |
| PUT `/api/SysDictType/{id}` | `Guid id, [FromBody] UpdateItemRequest<SysDictTypeDto> dto` | `Task<ServiceResult<SaveItemResult>>` | `System.Dictionary.Edit` |
| DELETE `/api/SysDictType/{id}` | `DeleteItemRequest id` | `Task<ServiceResult<SaveItemResult>>` | `System.Dictionary.Delete` |

契约源码：[SysDictTypeDto](../SwiftExport.Contracts/DTOs/SystemDtos/Dictionaries/Types/SysDictTypeDto.cs)、[SysDictTypeBatchSaveResponse](../SwiftExport.Contracts/DTOs/SystemDtos/Dictionaries/Types/Responses/SysDictTypeBatchSaveResponse.cs)、[SysDictTypeSearchRequest](../SwiftExport.Contracts/DTOs/SystemDtos/Dictionaries/Types/Requests/SysDictTypeSearchRequest.cs)、[SysDictTypeBatchSaveRequest](../SwiftExport.Contracts/DTOs/SystemDtos/Dictionaries/Types/Requests/SysDictTypeBatchSaveRequest.cs)、[DictTypeGroupTreeDto](../SwiftExport.Contracts/DTOs/SystemDtos/Dictionaries/Types/DictTypeGroupTreeDto.cs)。

### WarehouseSitesController

源码：[WarehouseSitesController](../SwiftExport.WebApi/Controllers/Inventory/Warehouses/WarehouseSitesController.cs)。

| HTTP / Endpoint | 请求参数 / DTO | 响应声明 | 权限 |
| --- | --- | --- | --- |
| GET `/api/inventory/warehouse-sites/picker` | ` [FromQuery] PagedRequestBase request, CancellationToken cancellationToken` | `Task<ServiceResult<PagedResponseBase<WarehouseSitePickerDto>>>` | `Inventory.Warehouse.View` |
| POST `/api/inventory/warehouse-sites/candidates/search` | ` [FromBody] PagedRequestBase request, [FromServices] IWarehouseCandidateQueryService candidates, [FromServices] ILogger<WarehouseSitesController> logger, CancellationToken cancellationToken` | `Task<ActionResult<ServiceResult<PagedResponseBase<WarehouseCandidateDto>>>>` | `Procurement.Order.Create 或 Procurement.Order.Confirm` |
| POST `/api/inventory/warehouse-sites/search` | `[FromBody] WarehouseSiteSearchRequest request, CancellationToken cancellationToken` | `Task<ServiceResult<PagedResponseBase<WarehouseSiteDto>>>` | `Inventory.Warehouse.View` |
| POST `/api/inventory/warehouse-sites/suppliers/search` | `[FromBody] PagedRequestBase request, CancellationToken cancellationToken` | `Task<ServiceResult<PagedResponseBase<WarehouseSupplierLookupDto>>>` | `Inventory.Warehouse.View` |
| GET `/api/inventory/warehouse-sites/{uid:guid}` | `Guid uid, CancellationToken cancellationToken` | `Task<ServiceResult<WarehouseSiteDto>>` | `Inventory.Warehouse.View` |
| POST `/api/inventory/warehouse-sites/supplier-virtual` | `[FromBody] CreateSupplierVirtualWarehouseCommand command, CancellationToken cancellationToken` | `Task<ServiceResult<WarehouseSiteDto>>` | `Inventory.Warehouse.Manage` |
| PUT `/api/inventory/warehouse-sites/{uid:guid}` | `Guid uid, [FromBody] UpdateWarehouseSiteCommand command, CancellationToken cancellationToken` | `Task<ServiceResult<WarehouseSiteDto>>` | `Inventory.Warehouse.Manage` |
| POST `/api/inventory/warehouse-sites/{uid:guid}/deactivate` | `Guid uid, [FromBody] DeactivateWarehouseSiteCommand command, CancellationToken cancellationToken` | `Task<ServiceResult<WarehouseSiteDto>>` | `Inventory.Warehouse.Manage` |
| POST `/api/inventory/warehouse-sites/{uid:guid}/activate` | `Guid uid, [FromBody] ActivateWarehouseSiteCommand command, CancellationToken cancellationToken` | `Task<ServiceResult<WarehouseSiteDto>>` | `Inventory.Warehouse.Manage` |
| DELETE `/api/inventory/warehouse-sites/{uid:guid}` | `Guid uid, [FromBody] DeleteWarehouseSiteCommand command, CancellationToken cancellationToken` | `Task<ServiceResult<WarehouseSiteDto>>` | `Inventory.Warehouse.Manage` |

契约源码：[CreateSupplierVirtualWarehouseCommand](../SwiftExport.Contracts/DTOs/Inventory/Warehouses/Management/CreateSupplierVirtualWarehouseCommand.cs)、[ActivateWarehouseSiteCommand](../SwiftExport.Contracts/DTOs/Inventory/Warehouses/Management/ActivateWarehouseSiteCommand.cs)、[DeleteWarehouseSiteCommand](../SwiftExport.Contracts/DTOs/Inventory/Warehouses/Management/DeleteWarehouseSiteCommand.cs)、[DeactivateWarehouseSiteCommand](../SwiftExport.Contracts/DTOs/Inventory/Warehouses/Management/DeactivateWarehouseSiteCommand.cs)、[WarehouseSupplierLookupDto](../SwiftExport.Contracts/DTOs/Inventory/Warehouses/Query/WarehouseSupplierLookupDto.cs)、[WarehouseSiteSearchRequest](../SwiftExport.Contracts/DTOs/Inventory/Warehouses/Query/WarehouseSiteSearchRequest.cs)、[WarehouseSitePickerDto](../SwiftExport.Contracts/DTOs/Inventory/Warehouses/Query/WarehouseSitePickerDto.cs)、[WarehouseSiteDto](../SwiftExport.Contracts/DTOs/Inventory/Warehouses/Query/WarehouseSiteDto.cs)、[WarehouseCandidateDto](../SwiftExport.Contracts/DTOs/Inventory/Warehouses/Query/WarehouseCandidateDto.cs)、[UpdateWarehouseSiteCommand](../SwiftExport.Contracts/DTOs/Inventory/Warehouses/Management/UpdateWarehouseSiteCommand.cs)。

