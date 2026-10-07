# Web Agent 标准工作流与治理文档（草案）

日期：2026-10-07。本文只规划未来Web开发SOP，不新增Agent定义、不修改Router/AGENTS/Skill，不创建React项目。现行入口仍是[AGENTS](../AGENTS.md)→[Router](../.agents/router.toml)→工作流→专项Skill→源码。

## 1. 适用范围与角色

现有.NET 10 WPF Frontend规则不能直接覆盖React。Web实施开始前，应单独授权注册一个Web前端角色/工作流及小规模技能组；不是看到“frontend”关键词就运行WPF模板。业务规划仍Planner负责，后端/Contracts按既有职责处理，公共Grid/EditableCollection由明确公共设施负责人维护。

一个任务只选一个主角色。需要后端契约变更时先列接口、影响WPF/VSTO和验收，再按授权交接；普通Web页面任务不得顺手修改Application/Infrastructure/Core业务。角色选择不授予自动新建聊天、并行Agent或共享文件接管权限，沿用[路由治理](../.agents/references/架构治理与路由判定规则.md)。

建议未来默认写入范围：SwiftExport.WebApp/**及该应用测试；generated仅生成工具写。Contracts/WebApi变更须任务明确包含，不由“前端依赖”自动扩权。本轮只写指定六份文档。

## 2. 新模块SOP（以Product为例）

| 步骤 | 动作与必须读的入口 | 产物 / 通过标准 |
| --- | --- | --- |
| 1. 定界 | AGENTS、Router、命中工作流/Skill，git status与文件所有权；确认本次是规划/实现/审查 | 主角色、允许写入范围、成功标准；保留已有改动，不把demo删除当恢复任务 |
| 2. 读架构 | 当前工程/业务总纲、Web架构；Product主数据地图 | 知道Product/主型号/分类/报价职责，WPF主线仍有效 |
| 3. 读源码与调用方 | ProductController、ProductQuery/MaintenanceService、ProductSearch/BatchSave DTO、WPF ProductsViewModel及相关测试 | 真实Endpoint/DTO/权限/版本/原子性矩阵，不复制旧Refit CRUD |
| 4. 查OpenAPI | 固定schema与generator配置，检查对应paths/requestBody/response/security/enum/null；看生成client现有函数 | 所需模型和方法可直接用；缺口登记API-ID交后端任务，不用any/手写DTO蒙混 |
| 5. 查权限 | PermissionKeys/Catalog、EndpointPermissionFilter、me；确认read/create/edit/delete和相关候选权限 | 复用既有键，UI与服务器一致；禁止虚构products:write等新命名 |
| 6. 查抽象 | 搜索现有Form/Grid/lookup/transport/EditableCollection/query key/unsaved hook | 列复用对象；没有组件也只实现本次必需能力，不造第三套base framework |
| 7. 创建feature | 按Web目录建products，用mapper定义EditorRow/可写字段；route配置懒加载 | 生成DTO不含dirty，feature不复制公共Grid；中文职责注释符合触碰范围 |
| 8. List / Query | 用生成page函数；Query key含页/搜索/筛选；Ant Table受控分页；signal/debounce | 正确页码/总数、空状态/错误/取消，POST查询也useQuery，固定排序不冒充远端任意排序 |
| 9. Detail / Editor | 明确来自列表或详情的快照初始化，lookup/Ant Form，输入校验 | dirty时后台刷新不覆盖，Uid/Version/审计和联表字段不能直接编辑 |
| 10. Mutation / BatchSave | 纯builder构建Create/Update/Delete；冻结提交快照；首期提交中禁编辑；检查外层与逐行结果 | ClientKey对齐Uid/NewVersion，失败保留原值，Product原子批次不逐行随意accept |
| 11. 权限与错误 | 菜单/route/button精确键，400字段、401重认证、403刷新me、409比较、422阻断、429、未知结果 | 服务器仍拒绝；mutation不盲重试，没有CommandId契约的批量不伪造幂等 |
| 12. 导航与Grid | unsaved blocker、beforeunload；ShowRowNumber、选择与限定Ctrl+D | 切页/筛选保存或放弃可控，填充不能写只读字段，焦点与键盘验收 |
| 13. 最小验证 | typecheck、lint、build；EditableCollection状态表和feature集成；PoC浏览器闭环 | 具体命令/结果/版本记录，schema生成无未解释diff；失败不删断言 |
| 14. 审查交付 | 实际diff、文件/引用/生成差异、授权边界与遗留问题 | 只改任务文件，记录业务代码是否改变、运行/未运行检查；未经授权不部署/推送 |
| 15. 后续模块 | 先核验前一模块可复用点与差异，再以SupplierProduct/单据用例扩展 | 不复制Product特殊保存语义到全部业务，专用命令仍专用builder |

源码入口：[ProductController](../SwiftExport.WebApi/Controllers/Products/Catalog/ProductController.cs)、[ProductSearchRequest](../SwiftExport.Contracts/DTOs/Products/Catalog/Requests/ProductSearchRequest.cs)、[ProductMaintenanceService](../SwiftExport.Application/Services/Products/Catalog/Maintenance/ProductMaintenanceService.cs)、[WPF产品Feature](../SwiftExport.Client.Wpf/Features/Products/ProductList)、[现有主数据契约测试](../SwiftExport.Application.Tests/MasterData/MasterDataRouteContractTests.cs)。

## 3. 完成标准与测试分层

**文档/方案任务**：核查引用、源码事实、差距与验证限制；仅文档不要求npm/.NET全量构建，不因报告发现缺陷自动修代码。本轮即此类型。

**Web实现任务**：
- typescript编译、lint、Vite build；不能以Vite转译成功替代tsc。
- 纯状态测试：新增→删除、修改→原值、删除→恢复、reject、Uid重映射、NewVersion、原子失败与未知结果保留；不是只测setState镜像。
- Feature集成：请求包装/字段投影、分页参数、query cache隔离、service error、权限；生成client不能手改。
- 浏览器PoC：真实auth/me、View/写键差异、列表+批量保存+409、切页dirty、Ctrl+D、文件如使用则Blob/headers。
- 后端发生授权修改时，额外运行现有相关HTTP/权限/并发测试；真实PG的版本/事务/写入验证需要明确隔离实例与数据范围，mock不是并发验收。
- 未运行/失败记录原因和影响，不能说“功能完成已验收”；保留失败，不为过门禁加广泛skip。

依据[测试与验收总纲](../.agents/references/测试与验收总纲.md)。本轮不新增自动化测试或配置，只设计未来门禁。

## 4. AI快速入口与文档所有权

不建议同时维护architecture.md和本项目工程总纲的重复权威正文。以下给出用户所提文档主题的归属；批准后可用短入口文件链接这些权威内容，不复制数百行规范。

| 主题（示例文件名） | 推荐归属与应写内容 | 禁止 / 更新时机 |
| --- | --- | --- |
| architecture.md | 链接既有工程架构总纲、业务总纲、PG当前基线；新增Web与同一API的依赖图、已实现/目标区分 | 不重新声明SQL Server，不写未实施架构为当前事实；跨层职责变化才更新 |
| frontend-architecture.md | 以web-frontend-architecture为草案起点；批准后Web目录、依赖方向、技术版本策略、数据/草稿/Query边界 | 不与WPF技能混为共同模板；新依赖或公共组件变化时维护 |
| frontend-rules.md | 可作为Web规则短入口：strict、generated只读、Form/Query/mapper复用、中文注释、任务写入范围 | 不复制各Skill步骤；禁止万能BasePage、transport直接业务判断 |
| api-contract-rules.md | 以web-api-gap-analysis契约节为来源；真实URL/method、success/错误例外、enum/null/date、分页、版本、生成流程 | 不另造ServiceResult/分页包装，不把Refit旧定义当唯一事实 |
| permission-rules.md | 以web-permission-design为来源；PermissionKey唯一目录、me/路由/按钮/后端矩阵、保留Admin政策 | 不把module/group/navigation权限变成直接用户授权 |
| grid-editing.md | SwiftDataGrid与EditableCollection责任、状态转换、树/分页/结果未知、冻结提交快照 | 不把dirty移动进Grid，不复制每个feature私有builder |
| error-handling.md | transport错误分类、field定位、correlation、401草稿、409、OUTCOME_UNKNOWN、toast与页面错误分工 | 不解析中文message、不重试写入制造重复、不新建ProblemDetails第二协议 |
| refactoring-rules.md | 默认引用AGENTS的Surgical Changes；新增公共抽象需至少真实多个使用点，先找已有；审批范围与兼容检查 | 不借新Web清理旧WPF/Refit/历史实体，不为了理论改稳定API |
| 复用索引（中文文件） | 公共transport、query keys、lookup、Grid、collection、unsaved、logger路径与用例；越短越易维护 | 不变成另一套实现文档或宏大待办；新增真实复用件时追加 |
| 版本与决策记录 | 每个批准的关键选择写小ADR：问题、选项、源码证据、决策、验证与后续条件 | 不记录所有临时讨论，不把本轮建议自动当已批准实施 |

本轮六份草案是审计/规划交付，不自动成为新规则；未来治理Agent在明确授权后接线现有Router和Web技能组。每个规范只指定一个owner，工作流引用、Skill给写法与检查、源码说明真实实现；减少AI在多个文件中任意选一个“更方便”的规则。

## 5. 防止AI重复造与越界的强制检查建议

修改前输出一张小表：用户动作→已有Endpoint→DTO→PermissionKey→复用组件→原子性/Version/CommandId→验收场景。不存在能力就标待确认，不能为了完成页面发明API、状态或权限。

实现时禁止：
- 手写与generated DTO同构的ProductDto，或UI字段塞入传输DTO。
- 另写fetch/axios直连、第二个ApiError、第二套分页/结果/dirty框架。
- 普通页直接接管库存或业务事务、用UI计算状态覆盖服务器事实。
- 用query.invalidate后无条件reset dirty；新建Guid当作服务端一定会保留；自动提升Version。
- 用外层200即清空全部编辑、把原子批次改解释为部分成功、将写入断线解释为未提交。
- 手改generated、删除有效测试、静默加库、为了Refine更改API。
- 用“此前迁移任务”恢复SQL Server、复原废弃demo或发起数据库初始化。
- 未经用户授权创建其他聊天、并行Agent、push/deploy或接管共享文件。

这些规则应在Web技能入口和专项检查清单引用一次，不能再复制到所有feature README。当前依据：[AGENTS](../AGENTS.md)、[路由治理](../.agents/references/架构治理与路由判定规则.md)、[现有Command协议入口](../.codex/skills/aspdotnet10/application-document-command/SKILL.md)。

## 6. 第一阶段任务书拆解

| 工作包 | 负责边界 | 依赖与交付 |
| --- | --- | --- |
| 契约P0 | Backend/Contracts，后续授权 | 生成schema基础、auth/product所需成功/错误、enum/null/binary声明；兼容WPF/VSTO |
| 浏览器基线 | Web与部署配置负责者，后续授权 | 同源代理或明确CORS、HTTPS、session/me、公开配置 |
| 写入P0 | Backend，后续授权 | Product并发分类；不必先改所有CRUD，但禁在PoC采用有歧义入口 |
| 公共前端底座 | Web公共设施负责人 | transport、Query provider、permission投影、collection、Grid首期、unsaved |
| Product PoC | Web feature负责人 | 真实查询与原子批量保存、冲突、导航、键盘行为 |
| 验证包 | 任务主控按真实影响选择 | schema/TS/feature/浏览器结果；PG写范围另明确，既有能力不能由文档冒充验收 |

完成后只扩已证明复用点。未决技术/业务问题在对应草案登记，不先新建统一审批、通用状态引擎或第三套模块框架。

