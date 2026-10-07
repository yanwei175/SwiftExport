# Web 前端 Skills 规划（草案）

日期：2026-10-07。本轮不创建Skill、不修改技能索引或Router；未来真正创建技能时使用skill-creator，并由治理任务按授权接线。本计划复用现有后端技能，Web专项不能覆盖WPF/Contracts兼容或后端事务规范。

## 1. 规模与入口

建议 **1个技能组入口 + 5个执行Skill**，不把react-query、api-client、openapi-client、batch-save、permission拆成几十个重复小文件。

```text
.codex/skills/react-web/
  index/SKILL.md
  feature-page/SKILL.md
  api-query/SKILL.md
  form-validation/SKILL.md
  grid-editing/SKILL.md
  auth-permission/SKILL.md
```

技能目录英文便于识别；SKILL.md使用工具固定名，docs/检查清单.md、examples/产品页面示例.tsx等给人阅读文件优先中文。入口按任务意图与目标对象选择适用子技能，不按首个关键词命中；与现有[后端入口](../.codex/skills/aspdotnet10/index/SKILL.md)和[WPF入口](../.codex/skills/wpf-dotnet10-design/index/SKILL.md)分别维护。这个新组尚未存在，不得声称已安装。

index包含：
- 适用React Web范围及明确禁止覆盖WPF/后端的边界。
- 读取AGENTS/Router/当前工作流及去重规则的引用。
- 路由表：页面→feature；生成/查询/错误→api-query；Form→form；Grid/脏状态/批量映射→grid-editing；登录/权限→auth-permission。
- 多命中补齐必要技能，仍只有一个主角色，不能自动开启并行任务。
- 所有子技能的短路径，以及公共transport/Grid/EditableCollection等真实实现入口，尚未实现只标规划。
- 适用后端契约/命令技能引用，不复制其全文，不创造Web专属第三套ServiceResult/Permission模型。

## 2. feature-page：模块页面与文件边界

**场景**：新增products、supplier-products、sales-orders等feature；列表/详情/Drawer、模块目录和依赖调整。不是皮肤换色或任意全仓重构入口。

**推荐写法**：
- 读模块地图→真实Endpoint/DTO/权限→现有公共组件，再建立页面。
- pages负责协调，queries/mutations负责远程交互，mapper区分DTO与EditorRow；只有实质转换才需api.ts。
- 以路由参数稳定Uid标识详情，筛选/页码可URL化；不把Form实例或编辑对象塞到跨页共享单例。
- 初始loading、empty、query error、detail not found及dirty导航都明确定义。
- UI中文职责注释、只触碰授权feature与必要公共文件。

**禁止写法**：页面手写fetch、复制ProductDto、万能BasePage/FeatureManager、目录空模板、依赖其他feature内部、为完成页面自行新增后端业务。

**示例（组织草案）**：

```text
products/pages/ProductManagerPage.tsx
  调用 useProductPage(search)
  用 mapper 创建 EditorRow 草稿
  把 rows/selection/onCellChange 传给 SwiftDataGrid
  调用保存 mutation；成功确认后更新collection并精确失效query
```

**检查清单**：
- [ ] 模块用例和写入范围明确，有对应源码/契约。
- [ ] query/mutation/mapper职责清楚；没有第三套公共基类。
- [ ] dirty未被焦点刷新覆盖；路由离开可取消。
- [ ] loading/空/错误/无权限可解释，类型检查、最小浏览器流程通过。

依据：[Web前端目录方案](web-frontend-architecture.md)、[ProductController](../SwiftExport.WebApi/Controllers/Products/Catalog/ProductController.cs)、[主数据地图](../.agents/references/模块能力地图/主数据模块能力地图.md)。

## 3. api-query：OpenAPI Client、Query与错误

**场景**：更新schema/生成client、连接GET或POST查询、mutation/失效、分页、网络取消和通用错误。将openapi-client、api-client、react-query、error-handling合并在同一协议责任下。

**推荐写法**：
- 使用固定schema和Orval版本，generated不可手改；调用已有Fetch操作。
- query key包含规范化筛选/页码/范围，POST搜索仍是query；signal透传。
- 一个transport负责Bearer、HTTP/error、correlation和二进制；Auth裸DTO与ServiceResult通过端点声明区分。
- feature mutation声明精确cache失效；写入不自动重试，CommandId协议另从后端技能确认。
- 401/403/409/422/429/503按稳定code分类，field定位由Form/Grid处理。
- nullable、enum、decimal/date与生成编译是实际验证，不用any或断言压住类型错误。

**禁止写法**：并行保留Orval和手写SDK、在component拼URL、把所有响应都解包data、只用HTTP200判断保存成功、用中文message做if、失效所有query后清dirty、为了生成改稳定API。

**示例（伪代码，实际函数名来自生成结果）**：

```typescript
// POST page属于读取；业务筛选和页码进入key，取消信号进入调用链。
const queryOptions = {
  queryKey: ["products", "page", normalizedSearch],
  queryFn: ({ signal }) => productPage(normalizedSearch, { signal }),
};
// 保存按真实语义处理；Product批量没有客户端CommandId，不能伪造自动重试幂等。
const mutationOptions = {
  mutationFn: saveProductBatch,
  retry: false,
};
```

**检查清单**：
- [ ] paths/method/request/response与Controller一致；生成+tsc通过。
- [ ] 已声明所需HTTP失败和JWT；serializer/schema样本一致。
- [ ] null/枚举/二进制不误解析；CancellationToken/AbortSignal作用范围清楚。
- [ ] 会话切换清cache；未知写入结果保留输入，不生成新命令盲重试。
- [ ] 日志脱敏且有correlation，错误只展示一次。

依据：[差距与生成器选择](web-api-gap-analysis.md)、[错误Mapper](../SwiftExport.WebApi/ErrorHandling/ServiceErrorHttpMapper.cs)、[已有命令Skill](../.codex/skills/aspdotnet10/application-document-command/SKILL.md)。

## 4. form-validation：Ant Form与字段转换

**场景**：普通资料/单据头表单，字段校验、lookup、服务器字段错误、日期和金额输入。避免Ant Form、React Hook Form和自定义表单引擎同时维护。

**推荐写法**：
- Ant Form规则做输入体验，feature validators纯函数处理跨字段输入；业务执行资格由服务端。
- DTO→Editor→DTO经显式mapper，文本、null、日期、金额语义按契约定义。
- 日期只在boundary转换；Date-only、本地墙上时间和UTC时刻不能混用toISOString。
- 服务器error.Field映射到真实路径；行错误由Grid显示，公共错误区保留不能定位的业务阻断。
- 提交只包含可写字段；审计、状态投影、交付/收货累计值保持只读。

**禁止写法**：完整展示DTO直接post、双份Form和collection分别各写同一字段、用JS浮点重算并覆盖服务端金额、把null全转空字符串、把所有DateTime补Z、重复添加第三方表单框架。

**示例（流程）**：

```text
Form输入 → 验证required/range → mapper构建专用请求
server error.Field="Header.CustomerId" → setFields到CustomerId
server code=BUSINESS_BLOCKED且无field → 页面业务错误区
```

**检查清单**：
- [ ] 可写字段明确，生成DTO与编辑类型无重复。
- [ ] 服务端字段路径能定位；不能定位仍可见，不吞错误。
- [ ] null/空串、金额/舍入、日期/时区已确认。
- [ ] 失败保留表单，dirty一致，权限/生命周期禁止编辑有效。

依据：[BaseDto](../SwiftExport.Contracts/DTOs/Base/BaseDto.cs)、[时间参数绑定](../SwiftExport.Infrastructure/Repositories/Base/PostgreSqlParameters.cs)、[SaveAfterSalesCommand](../SwiftExport.Contracts/DTOs/QualityControl/AfterSales/Maintenance/SaveAfterSalesCommand.cs)。

## 5. grid-editing：SwiftDataGrid、EditableCollection与BatchSave

**场景**：可编辑表格、树表、选择/行号/Ctrl+D、草稿变更、恢复、通用BatchSave或专用单据builder。Grid与collection同一Skill说明边界，避免两个Skill互相重复全部状态规则。

**推荐写法**：
- Grid受控呈现、emit意图，collection持immutable baseline和草稿，feature完成字段patch与请求mapper。
- ClientKey稳定；通用新增Uid可能被服务端替换，成功按ClientKey接纳Uid/NewVersion。
- created/updated/deleted互斥，按可写规范化投影与baseline比较；新增删掉消失，恢复原值转clean。
- 删除池保存原始与删除前编辑态；reject回baseline；tree为flat存储+渲染派生。
- dirty期间切服务端页/筛选需要保存/放弃/取消；不把未加载行当删除。
- 保存时冻结提交/禁编辑，原子批次整体接纳，明确部分成功才逐项接纳，未知结果不accept。
- Ctrl+D范围、可写列、权限、校验和焦点清楚，拒绝填Uid/Version/审计。

**禁止写法**：Grid调用API/组BatchSave、RowIndex作主键、一律AcceptChanges、用当前DTO.Version代替baseline版本、自增Version、假定树父删自动级联、普通BatchSave强加CommandId、单据统一降为通用CRUD。

**示例（状态表）**：

| 操作 | created | updated | deleted | isDirty |
| --- | --- | --- | --- | --- |
| 已有行A→B | 无 | 该行 | 无 | true |
| 再改B→A | 无 | 无 | 无 | false |
| 新建X→删除X | 无 | 无 | 无 | false（无其他变更） |
| 已有行删除→restore | 无 | 按删除前修改态决定 | 无 | 按比较决定 |
| 批量写失败/未知 | 保留 | 保留 | 保留 | true |

**检查清单**：
- [ ] 上述转换有纯状态测试；baseline对象不与草稿共引用。
- [ ] ClientKey唯一、响应完整、Uid/ParentId映射及NewVersion验证。
- [ ] 原子性由服务源码确认，不能从BatchSaveResponse类型推断部分成功。
- [ ] 保存失败保留草稿；Result未知不会盲重发；有CommandId则复用原意图。
- [ ] 服务端分页/树/键盘/未保存导航有针对性浏览器验证。
- [ ] feature专用请求没有被通用builder改变业务边界。

依据：[collection完整方案](web-frontend-architecture.md)、[BatchSave包装](../SwiftExport.Contracts/Wrappers/BatchSaveRequestBase.cs)、[SaveItemResult](../SwiftExport.Contracts/Wrappers/BatchSaveResponseBase.cs)、[WPF BaseListModel](../SwiftExport.Client.Wpf/Mvvm/BaseListModel.cs)。

## 6. auth-permission：会话与统一PermissionKey

**场景**：登录/me、路由/菜单/按钮、退出与401/403、权限键投影更新。

**推荐写法**：
- 现有Bearer+me，token存储策略明确；首期会话内存方案，刷新重登录是已知体验。
- 只按me.Permissions精确匹配；同一代码目录投影供Web/WPF/Backend共用。
- route要求View/Read，mutation按实际Create/Edit/Delete/Maintain/Confirm键，模块分组不授予子操作。
- 登出清Query cache和私有编辑上下文；401保留同会话内草稿直到用户决定，恢复须同一用户。
- RBAC/文件Admin保留政策与服务内授权显式说明，不把IsAdmin做全应用万能判断。
- 未登记端点交后端补矩阵，不绕过EndpointPermissionFilter。

**禁止写法**：JWT roles claim作为实时权限、角色中文名判断、localStorage默认永久token、token放query、另造ModulePermissions、由sys_view_route推导授权、普通用户初始化强制访问管理员permissions目录。

**示例（逻辑草案）**：

```typescript
// PermissionKeys投影必须与Contracts代码目录校验；没有角色名或通配符兜底。
const canView = permissions.has(PermissionKeys.ProductsProductView);
const canSave =
  (!hasCreates || permissions.has(PermissionKeys.ProductsProductCreate)) &&
  (!hasUpdates || permissions.has(PermissionKeys.ProductsProductEdit)) &&
  (!hasDeletes || permissions.has(PermissionKeys.ProductsProductDelete));
```

**检查清单**：
- [ ] login/me真实响应类型有schema；前端不能单靠JWT解码宣布认证有效。
- [ ] View与写入键分开，混合BatchSave检查全部实际键。
- [ ] 保留权限/最后管理员/账号状态由服务器控制，UI无绕过。
- [ ] 401/403与用户切换不泄漏缓存/草稿，服务器仍独立拒绝。
- [ ] TypeScript键与PermissionCatalog一致，未知键无授权。
- [ ] DevUser关闭后验证登录/匿名，未把开发免登录当Web认证完成。

依据：[权限设计](web-permission-design.md)、[MeDto](../SwiftExport.Contracts/DTOs/Auth/Session/MeDto.cs)、[PermissionCatalog](../SwiftExport.Contracts/DTOs/Auth/Permissions/PermissionCatalog.cs)。

## 7. 落地顺序与Skill验收

先批准Web工作流/技术栈与P0边界，再建立index、feature-page、api-query；第一次Product PoC需要form-validation、grid-editing、auth-permission同步齐备。无需再增加单独batch-save/openapi-client/permission/error-handling碎片技能；公共设施实际成熟后把真实示例路径回填，而不是在Skill里维护第二套完整实现。

每个Skill固定包含frontmatter（name/description）、适用/不适用、唯一权威引用、推荐/禁止、一个短示例、必要检查清单。示例与当前生成client/API同步，旧示例失效须更新，不能把schema手改绕过generated规则。

创建后验证：skill-creator结构校验、路径存在、触发无WPF/Web冲突、引用去重、典型任务路由场景、实际Product闭环。Skill通过静态校验不等于功能通过；本轮没有创建或执行这些Skill，也没有修改现行AI开发入口。

