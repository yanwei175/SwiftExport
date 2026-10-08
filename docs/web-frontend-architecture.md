# Web 前端架构与编辑基础设施（方案基线，未实施）

日期：2026-10-07。推荐新建独立 `SwiftExport.WebApp`，本轮不创建应用或修改现有WPF。业务事实仍由同一SwiftExport.WebApi、Application、PG事务负责。现有源据与差距见[盘点](web-readiness-audit.md)、[差距](web-api-gap-analysis.md)，权限见[统一权限设计](web-permission-design.md)。

2026-10-08 同步：脚手架/目录以 [总决策](Web客户端架构与脚手架决策.md) 为权威，内部多标签为首期要求；本文负责 Grid、编辑集合和模块数据边界。首批九个端点的运行证据见 [接口验收](Web首批接口契约验收.md)，不再将 Product 必须改为 409 作为写入前提。

## 1. 最小技术栈与版本策略

| 项目 | 推荐 | 边界 / 原因 |
| --- | --- | --- |
| Node / npm | Node 24 LTS，同一补丁版本用于开发/CI；npm使用随选定Node提供的版本并固定 | 用.nvmrc或.node-version、engines/packageManager、package-lock.json记具体版本；npm ci可重复安装。不要在任务中随意改包管理器 |
| 构建 | React Router 官方基础模板，Framework SPA，Vite 构建 | ssr:false；构建期根渲染须安全，生产静态产物；独立于 .NET publish |
| React | 支持稳定 Activity 的 React 19 系列，React/react-dom/types匹配并锁定 | 用实际骨架验收确定 patch，不默认引实验 API/Compiler |
| TypeScript | 当期开工时稳定、与Vite/Orval兼容的一条主线，锁定版本 | strict、noUncheckedIndexedAccess；DTO由schema生成，类型检查单独执行 |
| UI | Ant Design 6稳定系列 + 官方icons | Form/Table/Drawer/Modal/ConfigProvider；不同时加另一套组件库或Ant Design Pro框架 |
| 远程状态 | TanStack Query v5 | query负责服务器快照，mutation负责动作和cache失效；不存未保存编辑行 |
| 路由与工作区 | React Router Framework SPA + 内部多标签 Workspace | 框架负责应用入口；Workspace 管理业务页签、关闭与显式懒加载；Query 不与 loader 建两套缓存 |
| API | Orval生成Fetch操作函数和DTO；统一transport/custom mutator | 不默认Axios；生成hooks可后续选择，不能生成后又手写同样的HTTP方法 |
| 表单 | Ant Design Form + rules + feature纯验证函数 | 普通表单不再加React Hook Form/Formik/Zod；只有实际复杂跨对象验证需求才评估schema库 |
| 日期 | Day.js + Intl | 与Ant DatePicker协作，boundary转换；业务日期与UTC时刻分开，插件仅按真实需要引入 |
| 状态管理 | React useState/useReducer/Context | 工作区只管页签描述/摘要，feature 管各页签草稿；首期不加 Redux/Zustand |
| 国际化 | 首期中文zh_CN locale、集中消息；多语言确认后react-i18next | 业务字段显示名与HTTP错误码分开；不把大规模翻译工程作为PoC条件 |
| 权限 | me.Permissions +本地路由/操作映射 | 精确匹配同一PermissionKey，服务器最终授权；不引CASL或自定义权限树 |
| 错误 | 一个ApiError模型、Query/mutation错误展示、路由ErrorBoundary | 401/403/409/422/429/503分类，保留草稿；不吞异常或反复Toast |
| 日志 | 轻量集中logger，production脱敏 | Code/status/correlation/use case，不记录token/password/整单敏感DTO；暂不默认外部监控SDK |
| 测试 | Vitest、Testing Library；Playwright覆盖关键浏览器闭环 | Fetch mock按需用MSW；EditableCollection纯逻辑测试优先，不每个转发函数机械补测试 |
| 规范 | ESLint flat config + typescript-eslint + React hooks规则 + Prettier | 禁any逃逸/手改generated；import边界规则应针对已有边界，不一次堆数十插件 |
| 环境 | .env.example + VITE_API_BASE_URL等公开配置 | VITE_*进入浏览器包，不能放JWT签名key/DB密码；部署差异先采用构建时公开配置 |
| 发布 | npm ci→generate/check→tsc→lint→test→build；HTTPS静态站+同源/api代理 | 部署/推送需单独授权；React深链接fallback必须避开/api和图片资源，hash assets长缓存、index短缓存 |

依据：2026-10-07读取[Node版本策略](https://nodejs.org/en/about/previous-releases)、[Vite运行要求](https://vite.dev/guide/)、[React版本](https://react.dev/versions)、[Ant Design](https://ant.design/docs/react/introduce/)、[TanStack Query](https://tanstack.com/query/latest/docs/framework/react/overview)、[React Router模式](https://reactrouter.com/start/modes)、[Ant日期控件](https://ant.design/components/date-picker/)、[Vite环境变量](https://vite.dev/guide/env-and-mode)。这里选择系列和锁定策略，不把在线最新版patch写成已安装或已经完成兼容性验证。

**不建议首期使用Refine。** 它的data provider/resource/action约定还需映射现有POST查询、BatchSave逐项结果、整单CommandId/Preview/反向。当前已有Query、路由、权限与生成Client，加入Refine会再加一层对接。只有以后大量标准CRUD页面且试点证明能明显减少维护时才评估，不为它改后端。依据[Refine Data Provider](https://refine.dev/core/docs/data/data-provider/)与当前[BaseController](../SwiftExport.WebApi/Controllers/Base/BaseController.cs)、[PackingListController](../SwiftExport.WebApi/Controllers/Shipping/PackingLists/PackingListController.cs)。

## 2. 目录与依赖方向

唯一完整目录蓝图见 [脚手架决策第 5 节](Web客户端架构与脚手架决策.md)。保留框架默认 app 源码根，不同时维护另一套 src/app 模板；下列是逻辑职责摘要，尚未创建工程。

| 放置点 | 应放 | 禁止 |
| --- | --- | --- |
| app/root、routes、bootstrap、layouts、navigation | 框架入口、Provider、主壳与导航组装 | 产品保存 DTO、全部模块业务逻辑 |
| app/workspace | 页面目录、稳定 tabId、激活/关闭协调、状态摘要、显式懒加载 | 完整业务草稿、通用 saveAll 事务、取消写入后宣称回滚 |
| app/features/* | 该业务页面、页签控制器、query/mutation、mapper、validator | 导入 workspace 内部实现；跨 feature 深层引用；直接访问 DB |
| app/shared/api/generated | schema 导出的模型和 HTTP 操作 | dirty/selected/React 状态；手改生成文件 |
| app/shared/api | 网络协议、会话头、HTTP 错误/Blob | SKU 规则、库存数量、BatchSave 合并、角色判断 |
| app/shared/workspace | 上层注入的页签参数、状态报告和关闭契约 | 引用具体业务模块、另造 tab manager |
| app/shared/ui/swift-data-grid | 显示、选择、编辑意图、键盘 | 调 API、持有 dirty、组 BatchSave、自动赋 Version |
| app/shared/editing | baseline、变更集、恢复、结果接纳；React 适配 | HTTP、弹窗、权限授权、库存/单据生命周期 |
| app/shared/session、authorization | 身份快照、精确键匹配 | JWT 解码角色即授权、客户端 IsAdmin 万能放行 |
| app/shared/format | 已被真实用例需要的日期/数字纯转换 | 万能 utils/service/manager、DTO 副本 |

依赖方向是应用组装 → feature → shared；组装也可直接使用 shared。产品候选查询使用生成操作和规范 query key，不导入其他维护页的内部 hook。手写文件优先中文，工具固定入口使用标准名称；不机械生成空模块。

源码理由：[Contracts netstandard2.0](../SwiftExport.Contracts/SwiftExport.Contracts.csproj)保留 WPF/VSTO 共享兼容性；[NavigationRouteRegistry](../SwiftExport.Client.Wpf/Navigation/NavigationRouteRegistry.cs)说明菜单与权限不等于服务端动态页面；[ProductSearchRequest](../SwiftExport.Contracts/DTOs/Products/Catalog/Requests/ProductSearchRequest.cs)与[SaveSalesOrderRequest](../SwiftExport.Contracts/DTOs/SalesOrders/Draft/SaveSalesOrderRequest.cs)体现查询/写命令/编辑模型需要不同责任。

### 首期多标签工作区

业务入口统一登记到 workspace 页面目录，以 descriptor 传入业务身份。列表按 pageKind 去重，已有单据按 pageKind + Uid 去重，新单据用独立临时身份；React key 使用稳定 tabId。切换保留输入和滚动，不执行关闭确认；关闭、离开工作区和刷新分别保护全部相关草稿。

Ant Tabs 控制页签栏；稳定 React Activity 保留隐藏展示状态并清理展示 Effects。控制器/保存/关闭保护在 Activity 外，通过 shared 窄契约接宿主，不能因隐藏注销守卫。URL 只定位活动页，隐藏页不读取全局 useParams/useSearchParams。该组合尚待浏览器实测，完整规则与验收见 [多标签工作区](Web多标签工作区设计.md)。

## 3. 查询、表单、权限和错误数据流

```mermaid
flowchart LR
  Q[Query服务器快照] --> M[feature mapper]
  M --> E[useEditableCollection草稿]
  E --> G[SwiftDataGrid与Form]
  G -->|编辑意图| E
  E --> B[feature请求builder]
  B --> C[生成API函数]
  C --> T[统一transport]
  T --> A[同一SwiftExport.WebApi]
  A --> R[保存结果与新版本]
  R --> E
  R -->|失效受影响查询| Q
```

Query key必须含规范化筛选/页码/业务身份与会话隔离；登出清缓存，不能让下一用户看到上一用户数据。POST page/search是读查询，也使用useQuery；Query function传递AbortSignal，debounce作用于提交的搜索词。日期/集合规范化不应改变服务器定义的业务含义。

编辑页从快照显式初始化草稿。后台刷新不能覆盖dirty集合；Table显示草稿而不是直接改Query缓存。保存成功采用服务器归一化快照与NewVersion，再失效相关列表/详情；首期保存中锁住编辑，避免提交旧值后误accept新修改。Query默认重试/焦点刷新需显式约束，依据[TanStack defaults](https://tanstack.com/query/latest/docs/framework/react/guides/important-defaults)：读请求可有限重试瞬时网络故障，401/403等不重试；写mutation默认禁盲重试。

普通Form用Ant规则做必填和范围体验；服务器error.Field映射到表单/行，跨业务规则仍服务端最终校验。金额保留输入文本，在mapper验证并转当前decimal JSON；展示用Intl，不把JS浮点计算当权威金额。若业务要求超出JS number安全范围/精度，应先确认金额契约，再选择十进制库或字符串传输，不能自改后端schema。

统一错误至少携带status、code、message、field、correlationId、是否结果未知。400定位输入；401终止身份且保留用户内存草稿供重新登录流程；403刷新me和权限；409提供刷新/比较，不自动覆盖；422展示业务阻断；429按Retry-After提示；503/OUTCOME_UNKNOWN保留原意图。500不得只显示“未保存所以可以直接重试”。依据[ServiceErrorHttpMapper](../SwiftExport.WebApi/ErrorHandling/ServiceErrorHttpMapper.cs)、[GlobalExceptionMiddleware](../SwiftExport.WebApi/ErrorHandling/GlobalExceptionMiddleware.cs)、[WPF ApiCallRunner](../SwiftExport.Client.Wpf/Services/Api/ApiCallRunner.cs)。

## 4. SwiftDataGrid边界与首期能力

用户提出的拆分合理，Web不需要复制全部WPF EnterpriseGrid。其选择/验证等模块职责可作行为参考，不能把WPF AttachedProperty结构搬到React。

SwiftDataGrid是Ant Table的受控组合：
- 接受rows、稳定getRowKey、columns、selection、pagination与cell errors；emit onCellChange/onRowSelectionChange/onFillDown，feature处理状态。
- ShowRowNumber只用于显示：平面分页为(pageIndex-1)*pageSize+index+1；树使用当前可见顺序或明确层级编号，不能当ClientKey/行ID。
- Editable Cell先实现文本、数值、日期、lookup；cell提交多字段patch经feature validator，不能直接写row对象。
- Ctrl+D：定义源为选择区首行，按当前可见顺序填至后续目标；首期无完整矩形区选择时限定为同一活动列。跳过非编辑列/行和禁用单元格，校验每个patch，阻止浏览器收藏快捷键仅限Grid内有效操作。文本编辑框内不抢快捷键。
- 禁止填Uid/Version/审计字段、已交付/已收货投影等只读事实；权限和生命周期限定由feature传入canEdit。
- Selection与activeCell为UI状态；持久选择只保存稳定rowKey，重载时核对存在性。
- TreeData仅渲染parent/children派生投影；排序/展开不改变业务父子关系。大数据先服务端分页，不先引虚拟化和完整Excel引擎。
- 键盘/焦点/屏幕阅读器、空状态、loading与行错误反馈属于组件质量；业务存储和未保存导航不属于Grid。

当前对照：[SwiftExport.Wpf.EnterpriseGrid](../SwiftExport.Wpf.EnterpriseGrid)、[BaseListModel](../SwiftExport.Client.Wpf/Mvvm/BaseListModel.cs)。ShowRowNumber/Ctrl+D必须有浏览器行为验证，组件库自带Table不自动提供ERP这些语义。

## 5. useEditableCollection模型

建议以纯reducer为状态内核，hook负责React绑定；只暴露当前真实所需API，不提前造插件/数据源框架。

```typescript
// 草案接口：ClientKey是会话内稳定行键，Uid可能在首次保存后被服务端替换。
type EditableIdentity = {
  clientKey: string;
  uid?: string;
  version?: number;
};

// originalItems是不可变已接受快照，items是可见草稿。
// created/updated/deleted是按clientKey推导的互斥集合，不再维护三套可漂移的副本。
type EditableCollection<T> = {
  originalItems: ReadonlyMap<string, T>;
  items: readonly T[];
  createdItems: readonly T[];
  updatedItems: readonly T[];
  deletedItems: readonly T[];
  isDirty: boolean;
  add: (item: T) => void;
  update: (key: string, patch: Partial<T>) => void;
  remove: (key: string) => void;
  reset: (snapshot: readonly T[]) => void;
  acceptChanges: (confirmedSnapshot: readonly T[]) => void;
  rejectChanges: () => void;
};
```

以上是设计草案，不是生成DTO。acceptChanges只能由已确认保存成功路径调用；reset用于首次载入或用户明确放弃重载，不能静默覆盖dirty。原值比较按可写字段normalize/project函数，排除UI selection/error/服务器联表展示字段；使用结构化比较而非整个JSON字符串机械比较。null与空串是否等价由字段契约决定。

### 状态转换与重要边界

| 场景 | 正确行为 |
| --- | --- |
| Guid新建行 | crypto.randomUUID生成ClientKey；需临时Uid作树关系时另外保留临时身份。通用PG AddAsync会重生成Uid，保存后用ClientKey→响应Uid映射 |
| Version | 保存Update/Delete用已接受baseline的版本；编辑值不能改变Version；成功用NewVersion/重读快照更新，失败不得增加版本 |
| 新增后删除 | 从created与可见items移除，不产生DeleteItemRequest；不得向服务端删除从未持久化的Uid |
| 既有行删除后恢复 | 删除池保留原实体与删除前草稿；restore恢复删除前编辑态，RejectChanges恢复baseline并清全部变更 |
| 修改后恢复原值 | 对可写投影重新比较，回到clean，不因“曾编辑”永久dirty |
| 修改后删除 | 只生成Delete；保留baseline Version；恢复删除可继续原修改 |
| 原始集合别名 | baseline必须与可编辑对象隔离，不能深层数组/对象共引用；immutable patch |
| Tree | 存储flat身份关系，渲染时构树；父子新增按后端协议提交，保存后重映射ParentId；删除父不能自动级联猜测业务语义 |
| 行排序 | 展示排序不是业务修改；只有服务端可写SortNo改变才dirty |
| 服务端分页 | 首期dirty时禁止直接切页/筛选，提供保存/放弃/取消。不同页快照不能当完整集合，未出现行不等于删除 |
| 跨页编辑（以后） | baseline/变更按稳定身份保存局部片段；明确已加载范围、版本和服务端排序，不能通过合并页实现任意全局Ctrl+D |
| 页签切换/关闭/刷新 | 切换保留草稿；关闭由 Workspace 守卫；离开工作区用 router blocker，刷新用 beforeunload；保存失败保持；重认证只恢复同用户 |
| 结果不明 | 不accept、不reject；保留提交快照与原CommandId（有该契约时），重放/查询确认；普通BatchSave没有CommandId时核对当前持久状态后人工决定 |
| 后台数据刷新 | 若dirty保留本地并提示远端变化；不能在Query onSuccess中无条件reset |
| 保存时再编辑 | 首期禁用；以后若允许需提交快照/revision，只accept实际提交且确认的值，保留后续patch |

通用新增身份与分类树事实分别见[PostgreSqlDapperRepository.AddAsync](../SwiftExport.Infrastructure/Repositories/Base/PostgreSqlDapperRepository.cs)、[ProductCategoryService](../SwiftExport.Application/Services/Products/Categories/ProductCategoryService.cs)。分类保存通过logicalUid→persistedUid及父先子后处理，不表示所有树端点都有同一能力。

### 构建BatchSave

```typescript
// 示例伪代码：实际类型从generated引用，mapWriteDto排除所有UI/只读字段。
const request = {
  createItems: createdItems.map(row => ({
    clientKey: row.clientKey,
    data: mapWriteDto(row),
  })),
  updateItems: updatedItems.map(row => ({
    clientKey: row.clientKey,
    uid: baseline(row.clientKey).uid,
    version: baseline(row.clientKey).version,
    data: mapWriteDto(row),
  })),
  deleteItems: deletedItems.map(row => ({
    clientKey: row.clientKey,
    uid: baseline(row.clientKey).uid,
    version: baseline(row.clientKey).version,
    isSoftDelete: true,
  })),
};
```

RowIndex可带显示位置辅助用户定位，不能当响应关联主键。发送前校验集合互斥、ClientKey唯一、Uid非空、版本正数、必需权限；服务器仍做最终校验。SoftDelete选择沿用模块政策，不让Grid暴露任意硬删开关。

hook可通过纯mapper构建通用BatchSaveRequest，但销售SaveSalesOrderRequest、售后SaveAfterSalesCommand、采购/库存/PackingList仍由feature专用builder完成；不能为了复用hook把单据改成普通逐行CRUD。

### 保存结果协议

[BatchSaveResponseBase](../SwiftExport.Contracts/Wrappers/BatchSaveResponseBase.cs)包含Items、SuccessCount/FailedCount/AllSucceeded，**类型存在不证明该接口允许部分提交**。

1. HTTP非2xx/外层Success=false：不接纳任何行，不把本地推测的成功变更清掉。
2. 原子接口（已核对Product、ProductCategory、Supplier等整批事务）：仅整批确认成功且Items逐项成功才整体接纳。缺失/重复ClientKey、Operation不匹配、SuccessCount不一致视为协议异常，重读确认，不手动丢行。
3. 明确逐项提交接口：按ClientKey+Operation对齐，仅接纳已确认成功项；失败项保留草稿/删除池/error，新Uid与Version只对成功项更新。不能先全局accept再把失败行重新加回来。
4. 产品图片是明确部分成功，但它是ProductImageBatchUploadResultDto，不套SaveItemResult。multipart解析中途失败仍可能已有文件发布，不得以外层失败推断文件全回滚；应读回/逐项确认，禁止无条件整批重传覆盖。
5. 专用单据保存返回服务器详情/新头行版本时，以该专用响应为准，不强行解析BatchSaveResponseBase。
6. 网络断线或commit结果未知时，保留输入。具有BusinessCommandExecutor协议的请求重试复用同CommandId及内容；Product普通批量没有此协议，禁止赋一个前端随机id就认为获得幂等。

源码依据：[ProductMaintenanceService](../SwiftExport.Application/Services/Products/Catalog/Maintenance/ProductMaintenanceService.cs)、[SupplierService](../SwiftExport.Application/Services/Suppliers/Management/SupplierService.cs)、[ProductImageController](../SwiftExport.WebApi/Controllers/Files/ProductImageController.cs)、[ProductImageUploadService](../SwiftExport.Application/Services/Files/ProductImages/ProductImageUploadService.cs)、[BusinessCommandExecutor](../SwiftExport.Application/Commands/BusinessCommandExecutor.cs)。

## 6. 第一阶段PoC与扩展标准

选择**Product产品管理**，先列表、筛选、详情/lookup，再单页批量编辑、新增/修改/软删与冲突反馈；首轮引用已有分类/颜色/供应商/仓候选，不同时扩建它们的全部维护页。Product覆盖真实分页、复杂筛选、权限、Guid/Version、原子BatchSave且不用跨域库存过账，是比SalesOrder/PackingList更小的验证面。

PoC必须覆盖：me加载、View与Create/Edit/Delete分别授权、query取消、null/enum/date字段、临时Uid替换、恢复原值、新增后删除、失败保留草稿、当前真实的 422/BUSINESS_BLOCKED 陈旧版本拒绝、网络未知、dirty 筛选/分页确认、Ctrl+D 不能改 Uid/Version。HTTP 200 也要检查外层与行结果；没有结构化 VERSION_CONFLICT 时不解析中文消息猜冲突类型，也不自动覆盖重试。首批契约已验收，不要求先统一后端为 409。

骨架先通过两个编辑页签切换保留、隐藏页关闭守卫、保存回原页签及同实体去重验证；Product 闭环随后接入。真实 PostgreSQL 写入/并发和浏览器联调仍需实际验收，不由隔离 HTTP 测试代替。

第二步可选ProductCategory树验证父子新建与映射；第三步SupplierProduct验证报价字段与SupplierId作用域；第四步SalesOrder验证专用头行CommandId。TreeData、服务器分页、部分成功与复杂单据分别验收，不要求首个Grid同时证明所有场景。

本轮没有 npm 安装或 Web 实现；首批生成器实测由独立契约任务完成，见上述验收报告。目录、Workspace 与 Grid 仍为未实施方案，不改变现行 .NET 10 WPF 主线。

