# Web 客户端架构与脚手架决策

更新：2026-10-09。工程、主壳与公共编辑入口已建立，实际版本及分阶段证据见 [骨架验收记录](../SwiftExport.WebApp/docs/骨架验收记录.md)。本次按已确认计划补齐自动契约导出、12 小时会话恢复、缓存、偏好、诊断及编辑能力；本节规则是目标约定，当前整合验收另行记录，不以文档更新宣称通过。

## 1. 建议与边界

建议新建独立 `SwiftExport.WebApp`，采用 **React Router 官方脚手架 + Framework SPA Mode + React + TypeScript + Ant Design + TanStack Query + Orval Fetch**。React Router Framework Mode 的构建仍基于 Vite。项目独立安装、构建、测试和发布；后端是现有 ASP.NET Core HTTP API，开发断言绑定 PostgreSQL + Dapper。

这是针对本项目的方案选择，不是官方规定的 ERP 标准。早期 Data Mode 草案统一调整为 **Framework SPA Mode**，采用官方工程约定和路由类型生成；业务页签在工作区独立懒加载。编辑集合、权限权威和业务命令边界继续保留。接口行为以 [最新接口验收](Web首批接口契约验收.md) 为准，当前产品过期版本是 `422 / BUSINESS_BLOCKED`。相关资料已同步，治理入口已按授权接线；原生发现和业务实现的证据仍分别记录。

官方文档说明 React Router 的模式区别及 Framework Mode 的能力；React 官方的新项目指南也将 React Router 列为候选框架。采用 Framework Mode 不要求把 .NET 后端替换成 JavaScript 后端。[React 新项目指南](https://react.dev/learn/creating-a-react-app)、[React Router 模式选择](https://reactrouter.com/start/modes)

可预先稳定的是依赖方向、状态所有权、协议入口和发布边界。多文档页签现在就是首期需求；复杂跨页编辑等未来需求仍应按真实用例演进，不能承诺完全不重构。

| 事项 | 本轮形成的方案基线 | 详细权威入口 |
| --- | --- | --- |
| 独立工程与脚手架 | 同仓库独立 SwiftExport.WebApp；官方 React Router 基础模板，Framework SPA | 本文 |
| ERP 多标签工作区 | 内部页签保留草稿；稳定 tabId；关闭协调；隐藏不丢状态 | [多标签设计](Web多标签工作区设计.md) |
| 表格与编辑 | Ant Table + 受控 SwiftDataGrid；纯编辑集合；feature 专用请求映射 | [编辑基础设施](web-frontend-architecture.md) |
| API 契约 | 已验收 Orval Fetch；按实际 HTTP/内层结果消费，不强制改成 409 | [差距状态](web-api-gap-analysis.md)、[验收报告](Web首批接口契约验收.md) |
| 权限 | me.Permissions + 唯一 PermissionKey 目录；已打开页签也重新核权 | [权限设计](web-permission-design.md) |
| 开发流程与技能 | 独立 Web 角色/工作流及 1 个入口 + 5 个执行 Skill 已建立；运行时证据单独记录 | [SOP](web-agent-workflow.md)、[Skills 规划](web-skills-plan.md) |
| 发布 | 独立静态产物；Windows/IIS、Linux/Nginx 适配，同源 API 代理 | 本文第 9 节 |

## 2. 脚手架比较

先区分三个层次，避免把工程能力当作现成 ERP：

| 层次 | 解决的问题 | 本项目归属 |
| --- | --- | --- |
| 基础工程脚手架 | 工程创建、开发服务器、编译构建 | Vite 等构建基础 |
| 应用框架约定 | 入口、路由模块、类型生成、错误边界、构建约定 | React Router Framework SPA |
| ERP 应用架构 | 多标签、权限消费、草稿、批量保存、业务模块 | SwiftExport 自有架构 |

Framework Mode 的官方能力不能代替本项目的业务目录、权限模型或编辑机制。[React Router 模式说明](https://reactrouter.com/start/modes)

| 候选 | 能提供的基础 | 对本项目的代价 | 判断 |
| --- | --- | --- | --- |
| React Router 官方基础模板，Framework SPA Mode | 标准根路由/路由模块、路由类型生成、按路由拆分和静态 SPA 构建 | 仍须组织业务模块、API、权限与编辑状态；构建时根页面必须可预渲染 | **首选**：采用框架维护的工程约定，按下文补 ERP 边界 |
| Vite 官方 `react-ts` + React Router Data Mode | React/TS/Vite 基础工程；路由数据 API 可按需接入 | 路由装配、类型约束与懒加载更多由项目组织 | **比较记录**：当前不切换；有具体失败证据时走独立架构变更，不由实施 Agent 自动启用 |
| Ant Design Pro / Umi Max | 管理台模板、布局、菜单及 Umi 约定，可生成静态前端 | 改变既定 Vite/React Router 工程主线；模板有额外页面、插件及另一套 API 生成接线要裁剪 | **不作本次基座**：若用户更重视现成管理台页面，才重新评估；不是技术不兼容 |
| 社区完整 boilerplate，如 Bulletproof React | 可参考按 feature 聚合和单向依赖的经验 | 同时承接作者的依赖、样例业务和维护策略，无法替代本项目契约验收 | **用作组织参考**，不整体复制源码 |

依据：[React Router 官方安装与模板](https://reactrouter.com/start/framework/installation)、[Vite 官方模板](https://vite.dev/guide/)、[Ant Design 与 Umi 集成](https://ant.design/docs/react/use-with-umi/)、[Ant Design Pro 官方依赖清单](https://github.com/ant-design/ant-design-pro/blob/master/package.json)。当前 Pro 上游已包含 React 19、Ant Design 6、Query 和 Umi Max；不能用“只支持旧版本”否定它。这里没有安装或实测任何候选，优劣是结合当前项目约束的推断。

Ant Design 是组件库，Ant Design Pro 是管理台方案，两者不能混为一谈。选择 Ant Design 组件并不要求使用 Pro/Umi，也不要求为 ProTable 的数据模式改造现有 POST 查询或 BatchSave。

## 3. 采用框架约定，但不引入前端业务服务器

采用 React Router Framework SPA，是为了使用官方维护的工程入口、路由模块、类型生成和构建约定。后端仍为 ASP.NET Core；业务读取/提交由 TanStack Query 与生成 API Client 负责。Router 不建立第二套业务缓存，不承担业务保存编排。ERP 页签由 Workspace 管理，页面注册、参数类型、懒加载和生命周期按本文与多标签设计统一规定。

实施时使用官方基础模板，生成命令形式为 `npx create-react-router@<已核对版本> SwiftExport.WebApp`；实际 CLI、模板来源/提交及生成日期记入工程说明。首次可查 latest 来确定候选，确定后记录精确版本并提交 lockfile，不以未核验的固定版本伪称兼容。

关键配置建议：

```typescript
// 方案片段：关闭运行时服务端渲染，保持官方 app 与 build 目录约定。
import type { Config } from '@react-router/dev/config';

export default {
  appDirectory: 'app',
  buildDirectory: 'build',
  ssr: false,
} satisfies Config;
```

SPA Mode 构建生成入口 HTML；发布目标为客户端静态产物，通常为 `build/client`，以实际构建清单验收。构建机使用 Node，生产站点提供静态文件，不运行 React Router 的 Node 服务。[官方 SPA 说明](https://reactrouter.com/how-to/spa)、[官方配置约定](https://reactrouter.com/api/framework-conventions/react-router.config.ts)

**需要在第一次骨架验收就验证的边界**：`ssr:false` 仍会在构建时渲染根页面。根页面和初始导入链不能在顶层或初次 render 中访问 window/sessionStorage，也不能在构建时读取登录身份或访问业务 API。根 HydrateFallback 使用无身份依赖的加载界面；会话恢复、me 查询和 DOM 操作放在浏览器阶段。路由的业务数据不采用构建期 loader。官方模板若带 Tailwind/示例欢迎页、服务端启动或容器内容，按本项目实际需要裁剪，不保留第二套 UI 主线。

## 4. 技术栈与唯一职责

| 技术 | 保留的职责 | 本项目决定 |
| --- | --- | --- |
| React / TypeScript | 组件与类型约束 | React 19 稳定系列；TS 与模板及已验证 Orval 兼容，实施时锁精确版本；strict 与 noUncheckedIndexedAccess |
| Vite / React Router Framework | 开发构建、路由、路由类型、错误边界与拆分 | SPA；使用框架入口，不再手工并建 createBrowserRouter 或第二个 RouterProvider |
| Ant Design | Form/Table/Layout/Drawer/Modal、主题和中文 locale | Ant Design 6；标准控件直接使用，只包装有明确项目职责的部分 |
| TanStack Query | 服务器快照、读取去重、缓存失效、提交状态 | 业务数据的唯一客户端缓存所有者 |
| Orval Fetch | OpenAPI DTO 与 HTTP 操作函数 | 复用已验收生成策略，保留状态联合类型及路径编码；不手改生成文件 |
| React 状态 / Ant Form | 页面草稿、选择、展开、表单输入 | 不把草稿放进 Query 缓存；首期无额外全局状态库 |
| Day.js / Intl | DatePicker 值与显示格式 | 按字段的日期/时间语义转换，不统一强加 UTC |
| Vitest / Testing Library / Playwright | 纯逻辑/组件/关键浏览器场景 | 按风险验收，Mock 需要时用 MSW，不强制每个文件都有镜像测试 |
| ESLint / Prettier | 类型和 Hooks 规则、依赖边界与格式 | 不直接照搬其他模板的整套插件；生成文件排除手写规则 |

Ant Design 官方提供 Vite 集成说明；Query 官方明确区分服务器状态与客户端状态。本项目的“缓存不承接编辑草稿”是对该边界的应用。[Ant Design 与 Vite](https://ant.design/docs/react/use-with-vite/)、[Query 的状态职责](https://tanstack.com/query/latest/docs/framework/react/guides/does-this-replace-client-state)

早期契约工具使用 Node 24、Orval 8.40.0/TypeScript 7.0.2；Web 工程按实际框架 peer 约束采用独立锁文件，准确版本及联合检查见 [工程入口](../SwiftExport.WebApp/docs/工程入口与命令.md) 与 [骨架记录](../SwiftExport.WebApp/docs/骨架验收记录.md)。不将工具的旧候选版本当作当前工程版本，不自行同时升级全部库。

## 5. 建议目录

以下为**目标蓝图**，本轮没有创建这些目录。骨架阶段只创建有文件、有真实职责的目录，不为所有模块生成空页和空 services。框架/工具识别的固定文件名沿用标准；手写职责文件和说明文档优先中文。

```text
SwiftExport.WebApp/
  package.json / package-lock.json
  react-router.config.ts          # SPA、构建和路由框架配置
  vite.config.ts                 # 本地代理、构建配置；不放生产密码
  tsconfig.json
  eslint.config.js
  orval.config.mjs
  vitest.config.ts
  playwright.config.ts
  .env.example / .gitignore
  public/                        # logo 等公开静态资源，不放业务附件
  openapi/
    首批接口.json                # 可审核快照，来源为后端验收产物
  scripts/
    核对生成产物.mjs              # Node 跨平台脚本，不依赖 PowerShell 运行
  deploy/
    windows/                     # IIS 静态站、代理和页面回退配置
    linux/                       # Nginx 对应配置；不含凭据
  docs/
    工程入口与命令.md
    目录职责与依赖规则.md
    业务页面开发流程.md
  app/                           # 保留 React Router appDirectory 约定
    root.tsx                     # 框架根、全局错误边界与初始加载界面
    routes.ts                    # 唯一 URL 与路由模块清单
    routes/
      登录路由.tsx               # 薄适配：接框架参数，组合登录页面
      ERP布局路由.tsx            # 会话等待、访问控制与 Outlet
      工作区路由.tsx             # URL 与工作区入口，不逐个路由卸载业务页
      未找到路由.tsx
    bootstrap/
      应用Providers.tsx          # Query、Ant、会话依赖组合
      会话初始化.ts              # me 加载与身份切换生命周期
    layouts/
      ERP布局.tsx                # 主壳、响应式导航和全局反馈
    navigation/
      导航目录.ts                # 标签、分组、pageKind；权限取页面目录
    workspace/
      工作区状态.ts              # 页签描述、顺序、activeId 与状态摘要
      工作区Provider.tsx
      工作区视图.tsx             # Ant Tabs、激活和关闭入口
      页签容器.tsx               # 稳定 tabId、控制器、Activity 与页签错误边界
      页面目录.ts                # pageKind、权限、显式 lazy import 与去重规则
      活动页地址.ts              # 活动页签的 URL 编解码，隐藏页不读全局参数
      关闭协调.ts                # 完整输入/保存/未知状态的关闭保护
    features/
      auth/
        pages/登录页.tsx
        queries.ts / mutations.ts
      products/
        产品页签入口.tsx          # 每个 tabId 的控制器与页面组合
        pages/产品管理页.tsx
        components/产品表格列.tsx
        components/产品编辑表单.tsx
        queries.ts / mutations.ts
        model/产品编辑模型.ts
        model/产品编辑状态.ts
        model/产品保存请求.ts
        model/产品输入校验.ts
        permissions.ts
      product-categories/        # 后续真实用例出现时建立
      supplier-products/
      sales-orders/
      purchase-orders/
      inventory/
      packing-lists/
      system/
    shared/
      api/
        generated/              # 生成 DTO 和 HTTP 操作，不人工编辑
        请求执行.ts              # 为生成函数提供统一请求参数/传输接线
        API错误.ts               # 稳定错误与结果未知信息，保留 trace
      workspace/
        页签描述.ts              # 页面参数与公共页签契约
        useWorkspaceTab.ts       # 上层注入关闭保护/状态报告接口
      session/
        会话上下文.tsx           # token/身份状态的公共访问契约
      authorization/
        权限检查.ts              # 现有 PermissionKey 精确匹配
      query/
        查询客户端.ts            # 默认读取/提交行为
        查询键.ts                # operation + 输入 + 身份范围
      ui/
        swift-data-grid/         # UI、单元格、选择与编辑意图
        lookup/                  # 通用候选呈现，接收查询/选择契约
        feedback/                # 错误与保存反馈
      editing/
        编辑集合.ts              # 通用纯状态内核，按实际用例实施
        useEditableCollection.ts # React 适配，不放 HTTP/业务规则
        未保存导航.ts            # Router blocker / beforeunload
      format/                   # 具体日期/数字边界函数，不建万能 utils
      config/公开配置.ts
      styles/                   # 基础样式、主题 token
    testing/
      测试Providers.tsx
      handlers/                 # 按需要模拟已声明的 API
  tests/e2e/                    # 浏览器闭环，组件/纯逻辑测试就近放置
```

这些路径中的 app 是框架源码根，逻辑上的“应用组装层”仅指 root/routes/bootstrap/layouts/navigation/workspace，不意味着 features/shared 也能任意互相依赖。查询与提交文件小时可以合并；编辑模型文件大到包含不同职责才拆分。不得为了目录整齐机械生成每个 feature 的同一套空文件。

## 6. 依赖规则比目录数量更重要

逻辑依赖方向：**应用组装层 → 业务 feature → shared**；应用组装也可直接依赖 shared。shared 不反向引用 feature 或 root，feature 不导入路由/主壳。

| 场景 | 允许方式 | 避免的问题 |
| --- | --- | --- |
| 产品页需要颜色/分类/供应商候选 | 产品查询边界调用生成读取操作；统一操作标识建立查询键 | 不直接导入另一个维护页的 ViewModel/内部 hook |
| 两个模块要组合 | 上层路由或组合组件传入必要数据/回调 | 不让 features 相互深层引用形成循环 |
| 真正共享的读取用例 | 先用同一生成操作和规范查询键；多处需要相同业务策略后再提取明确读取边界 | 不提前造通用 BaseService 或复制 DTO |
| 普通 Ant 控件 | 直接使用 | 不给所有控件套无意义 XxxBase 包装 |
| 公共表格 | feature 传入 rows、columns、权限约束和回调 | 表格不调 API，不拼 BatchSave，不维护 Version |
| 通用编辑集合 | 纯数据转换与状态；feature 提供可写字段和响应接纳策略 | 不把所有单据统一成 CRUD |
| 生成 DTO | feature 引用或映射成 EditorRow | 不同时维护另一份手写传输类型 |

### 6.1 代码归属与跨模块交互

| 代码 | 固定归属 | 禁止归属/职责 |
| --- | --- | --- |
| 导航、页签注册、模块组合 | 应用组装层 | 普通业务页面不得自建导航/标签管理器 |
| 产品保存流程、产品 DTO 映射 | features/products | 公共 Grid、通用请求层不得承载产品保存规则 |
| 当前用户权限检查工具 | shared/authorization | feature 不重写权限算法或根据角色名授权 |
| HTTP 执行、认证头、传输错误分类 | shared/api | 页面不散落 fetch/认证头/全局错误判断 |
| 通用增改删状态算法 | shared/editing | 不依赖产品 API，不持有服务器缓存 |
| 行号、填充、单元格编辑交互 | shared/ui 下的 Grid | 不负责事务提交、BatchSave 或回执接纳 |
| 产品列定义、字段输入校验 | 产品 feature | 不放进万能表格配置中心 |

产品页读取供应商候选，使用已声明的生成读取操作和 shared/query 的统一 Query Key；不导入供应商管理页的内部 hook 或编辑模型。产品页打开供应商详情，发出 Workspace 公共打开意图 `{ pageKind, params }`，由应用页面目录校验参数与权限、定位或加载目标；不导入供应商页面组件或操作工作区 reducer。读取候选不等于得到供应商详情的打开权限。

依赖检查须覆盖 shared 反向导入、feature 跨模块深层引用，以及 feature 对 app/workspace 实现的引用。真正跨模块组合归应用层；公共打开意图与注入契约归 shared/workspace，不引入新的模块全局状态。

目录设计参考了社区 [Bulletproof React 的 feature 聚合与单向依赖](https://github.com/alan2207/bulletproof-react/blob/master/docs/project-structure.md)。它是社区工程经验，不是 React 官方标准，也不整体复制其依赖。

另核对了 [Feature-Sliced Design 的层与导入约束](https://feature-sliced.design/docs/reference/layers)。FSD 明确允许仅采用需要的层；本项目借鉴职责和依赖思想，**不宣称完全采用 FSD**，也不照搬 pages/widgets/features/entities 的完整分层。这里的 feature 是 ERP 用例模块聚合，语义与 FSD 的交互切片不完全相同。

实施阶段用 ESLint 约束真实边界：shared 不得导入 features/routes/bootstrap；业务模块不得跨模块导入内部文件；框架 route 模块不得包含领域保存编排。按真实文件检查，不能只写规范。模块公开入口仅在需要时提供窄导出，避免一个总 index 将所有页面提前导入、破坏按路由拆分。

## 7. 状态、请求和路由的所有权

| 状态 | 所有者 | 约束 |
| --- | --- | --- |
| 已登录身份与 token 生命周期 | bootstrap + shared/session | 登录流程由 auth feature 发起；身份切换取消旧请求并清理对应缓存/草稿 |
| me 与权限快照 | Query / 会话初始化 | 权限来自 me，不根据角色显示名猜测；后端最终授权 |
| 产品、字典、候选查询结果 | Query | 同一操作/输入使用规范查询键，允许共享快照；按身份隔离 |
| 页签顺序、激活与描述 | workspace reducer + Context | 不存产品 DTO/表单草稿；只接收 [完整保护摘要与关闭接口](Web多标签工作区设计.md#workspace-contract) |
| 可分享的活动页签身份 | URL + workspace 地址解析 | 仅活动页签写 URL；隐藏页依照自己的 descriptor，不读取全局 route params |
| 页码与筛选 | 各页签的 feature 状态 | 切换页签保留；替换结果集遵守完整输入保护，不能只看业务 dirty |
| 当前编辑值 | Form 或编辑集合，按下文三种模式选择 | 同一字段只有一个当前值来源；草稿不进入 Query 缓存 |
| baseline Version、冻结提交、回执接纳 | feature 编辑会话 | 不被后台查询刷新覆盖，不提前增加 Version |
| 行选择、展开、活动单元格 | 页面/控件 React 状态 | 与持久业务事实、服务器权限分离 |
| 字段输入错误 | Ant Form / feature validator | 提供输入体验；不复制服务器跨业务状态决策 |

<a id="web-common-services"></a>

### 7.0 会话、缓存与公共设施基线

后端 JWT 仍在签发后 12 小时到期。Web 首期采用 HttpOnly 恢复 Cookie 保存同一 JWT，业务请求使用内存 Bearer；刷新或关闭浏览器重开先恢复原 JWT，再核实 me。不续签、不延长 exp，普通 WPF 登录和 Bearer 验证保持原行为。Cookie 必须 Secure、SameSite Strict、无 Domain，恢复范围限定 `/api/auth/browser-session`；开发及生产 Web 入口均使用 HTTPS 同源代理。退出清除浏览器恢复凭据，不宣称服务器撤销旧 JWT。保存、关闭、身份更换与退出的保护流程见 [多标签会话规则](Web多标签工作区设计.md#workspace-session)。

Query 只缓存服务器快照，键包含 userId、identityEpoch、permissionEpoch、规范操作及输入。业务查询默认 staleTime=0/gcTime=5 分钟；远程候选为 30 秒/10 分钟；低频参考数据为 5 分钟/30 分钟。读写不自动重试、查询不随窗口聚焦刷新、不默认轮询；活动且有读权的查询在重新启用、挂载或重连时核新。权限集合改变后先停止旧读取，取消并移除旧权限范围业务快照，再开放新范围；身份 epoch 仍表示草稿归属，权限 epoch 不重建同用户草稿。gcTime 是无观察者回收时间，不是内存容量保证，也不据此驱逐有保护摘要的标签。后台更新不覆盖 Form/Grid 的当前值或基线，明确放弃后的重载失败仍保留原会话。

localStorage 仅保存按已核实 userId 隔离、带版本的界面偏好：侧栏折叠、声明过的列布局及分页大小。不保存 token、权限、DTO、搜索内容、页签身份或草稿；损坏、不可用或配额失败降级当前页面内存状态，只操作本应用对应键。诊断最多 100 条内存白名单事件，限操作、阶段、状态、追踪标识等，无任意 payload、正文、凭据或自动上传；登出和换身份清理。诊断不替代业务恢复保全记录。

弹窗/message/notification 复用 Ant App 上下文。feature 映射服务器字段到 Form 路径、ClientKey/列或页级错误，未知字段仍显示，错误不参与 dirty。数值保留无效 raw 输入、不静默舍入；业务日期与时刻分别按字段契约转换，首期日期选择器 inputReadOnly。Grid 的受控 custom editor 共用只读/错误/取消/无障碍约束，填充仅发意图。候选搜索词与业务选值分离，远程搜索防抖 300ms，迟到结果不覆盖当前搜索，已有值缺候选仍保留；不扩大读取权限。头 Form 与行集合由一个 feature 会话统一校验、冻结和接纳。

文件/图片接口先经 OpenAPI 审核与生成，鉴权、读取及上传结果解释归 feature；受保护资源不得用泄露 token 的 URL。HTTP 缓存遵循真实端点响应，临时 Object URL 由创建者释放。实际适配在首个相关页面之前完成，不虚构文件服务或将恢复 JSON 导出当上传。

React 官方说明了避免冗余、重复和过深状态的原则；这里把互斥增改删集合从同一编辑状态推导，避免维护数套可漂移副本。[React 状态结构](https://react.dev/learn/choosing-the-state-structure)

Router 负责登录/工作区/错误入口及离开工作区的导航阻断；workspace 负责内部页签。Query 负责业务读取和提交，不同时建立 loaderData 和 Query 两套快照。Ant Form 的提交使用 feature mutation，不同时混用 Router action 执行同一次保存。页签控制器、关闭保护和保存状态位于 Activity 外；隐藏仅暂停页面展示副作用，不能丢草稿或取消已派发写入。具体生命周期见 [多标签设计](Web多标签工作区设计.md)。

routes.ts 只注册框架入口。workspace 页面目录统一 pageKind、权限、业务身份与显式 lazy import；导航目录引用 pageKind，活动地址编解码也查询同一页面目录。业务页不各建一个 URL 路由管理器，框架按路由拆分不能替代内部业务页签的显式懒加载。

**首期必须支持内部多标签工作区**：切换保留筛选、表单、编辑集合、未提交输入及滚动；同一已有单据重复打开激活原页签；新建单据各有独立临时身份；tabId 保存后仍稳定。关闭与离开保护统一遵守 [公共契约](Web多标签工作区设计.md#workspace-contract)，不能只看集合 dirty；切换不弹未保存确认、不强制校验。浏览器刷新不等于内部切换，跨刷新恢复草稿尚未纳入首期。

<a id="web-field-owner"></a>

### 7.1 字段值的唯一所有权

| 编辑场景 | 当前编辑值的权威来源 | 保存入口 |
| --- | --- | --- |
| 普通单对象表单 | Ant Form；feature 控制器持有基线、版本与保存状态 | feature 读取并校验 Form 值，再构建请求 |
| 批量编辑列表 | useEditableCollection | feature 从编辑集合生成 BatchSave |
| 单据头 + 多行明细 | 头字段属于 Form，行字段属于编辑集合 | 同一个 feature 编辑会话统一校验、冻结并提交 |

**WEB-EDIT-01：同一字段只允许一个当前编辑值来源。** baseline 和当前值可以分开保存；不允许 Form、useState、编辑集合各保留一份可写当前对象，再用多个 useEffect 双向同步。Form 实例随该 tabId 的编辑会话保留，展示子树挂载/恢复不重复用 initialValues 覆盖草稿；初始化、明确放弃或确认回执接纳才能显式重设基线/表单。

带 name 的 Form.Item 由 Form 接管值与变更；程序赋值使用 Form API。Grid 单元格临时缓冲拥有尚未转换/提交的控件原始输入，成功提交才进入编辑集合，不能成为另一份长期业务当前值。其保留与保护摘要遵守 [多标签公共契约](Web多标签工作区设计.md#workspace-contract)，不能因隐藏或尚未更新集合而丢失。[Ant Form 数据收集约定](https://ant.design/components/form/)

**WEB-EDIT-02：dirty 是参与保存字段的归一化投影与 baseline 的比较结果。** 比较排除 selection、展开、错误及只读联表显示字段；null/空串等价性按字段契约决定。改动后恢复原值必须回到业务 clean，touched/曾经编辑不能直接当 dirty。业务 clean 不等于没有待保护输入；未提交输入及关闭放行条件只在 [多标签公共契约](Web多标签工作区设计.md#workspace-contract) 定义。Form 的变更事件只触发比较或更新摘要，不复制一份 Form 当前值到控制器；集合增改删从同一编辑状态推导。具体集合算法参考 [编辑基础设施](web-frontend-architecture.md)，实际复用入口见 Web 工程公共设施说明。

## 8. API 接入不会要求页面层重写

已验收工具仍位于 [_tools/Web契约验证](../_tools/Web契约验证/使用说明.md)。该目录用于后端契约验收，不变成前端运行时依赖。Web 项目消费已审核的 schema 快照，持有自己的生成配置、lockfile 和 generated 产物；可从独立项目目录生成、构建，不依赖后端源码或 PowerShell 脚本在场。

后端改动 → 契约验收并导出 schema → 审核兼容差异 → 更新 Web 的 openapi 快照 → 生成 DTO/操作 → 类型检查 → feature mapper/query 调整 → 页面验证。HTTP API 是契约；数据库模型或 C# Contracts 项目不是浏览器依赖。

generated 管线路径只有一个；不同时引入 Orval 和另一套手写 client。现有 Orval mutator 委托唯一请求执行边界负责 Authorization、signal、错误事实及 trace，不包含产品规则。新增接入先导出实际 OpenAPI、核对已审核端点清单与差异、更新快照、生成并类型检查，再写 feature。PermissionCatalog.All 同批投影权限键快照与 TS 常量，不从 me 的普通 string 推导目录；产物自带于 Web，运行时不依赖 .NET 源码。传输层不依赖 React hook，也不自行跳转页面。

<a id="web-api-layers"></a>

### 8.1 唯一调用链与结果解释

```text
页面/页签控制器
  → feature queries / mutations
  → Orval 生成 HTTP 操作
  → shared/api 统一请求执行边界
  → ASP.NET Core API
```

| 位置 | 负责 | 禁止 |
| --- | --- | --- |
| 页面/页签控制器 | 用户动作、编辑会话、展示状态、调用 feature 保存入口 | 手写第二套 HTTP 操作或直接解读其他模块保存协议 |
| feature queries/mutations | 本业务结果解释、DTO 映射、回执核对/接纳、相关缓存失效 | 把草稿写入 Query 缓存、只按 HTTP 成功接纳 |
| 生成 HTTP 操作 | Endpoint、参数、请求 DTO、响应类型 | 手工修改 generated，另写同构传输 DTO |
| shared/api 请求执行 | 认证头、signal、网络/HTTP 状态、追踪信息 | 统一强行解包 data、吞掉 error/行失败、业务保存/跳转 |

**WEB-API-01：HTTP 成功不必然是业务成功，业务外层成功也不必然是每行保存成功。** 保留请求发送阶段、status、headers、完整正文以及网络/取消信息；裸 DTO、ServiceResult、原子整批与行级响应由各 feature 按实际契约解释。通用层不做 acceptChanges，不把所有 200 当整批成功，不按状态码统一判断写入是否生效，不分析中文 message 猜回滚、冲突或成功。提交分类与解锁条件唯一正文见 [多标签保存契约](Web多标签工作区设计.md#workspace-save)，具体 Product 证据/缺口见 [验收报告](Web首批接口契约验收.md#product-recovery)。

该调用链已有 Web 运行时 mutator，新增操作继续重新验证生成差异、状态联合类型、signal 和路径编码；不靠手改生成文件实现。仅明确的浏览器会话操作与 Web 模式 login 允许 same-origin credentials 且不携带旧 Bearer；普通业务保持 Bearer/omit。读取使用统一身份与权限范围 Query Key；提交关闭自动重试，错误和取消本身不证明服务端未写入。

必须保留指定历史验收范围的事实：login/me 是裸 DTO，其他首批成功多为 ServiceResult；Fetch 状态联合类型不能把非 2xx 当成功。Product 过期版本是 422/BUSINESS_BLOCKED，其业务拒绝展示不证明本次操作未生效，分类遵守上述真实端点证据。通用 CRUD 的外层成功与行失败不能被统一 transport 抹掉。写操作不开自动重试；Query 的默认读取重试/焦点刷新也需显式约束。[Query 默认行为](https://tanstack.com/query/latest/docs/framework/react/guides/important-defaults)、[现有验收报告](Web首批接口契约验收.md)、[Orval 输出配置](https://orval.dev/docs/reference/configuration/output/)

权限键由真实 `PermissionCatalog.All` 机器投影为审核快照及 TS 常量，浏览器精确核验已知键与 me 的交集；不能从 DTO 的普通 string 字段推导完整权限目录。生成 DTO 的日期字符串不是时区决策；金额 JSON number 不代表前端浮点计算可作为财务权威。编辑模型与 mapper 负责输入边界，服务器决定业务结果。

## 9. 独立发布与跨平台

初期同仓库但独立 package 是部署解耦，不需要先拆 Git 仓库或创建全仓 npm workspace。以后拆库时，Web 自带 schema、生成配置与公开环境说明即可独立构建。它不引用 .NET 项目，不随 WebApi publish 自动打包。

Windows 使用 IIS 静态站与 API 转发；Linux 使用 Nginx 对应配置。Web 发布包只含客户端静态文件与发布清单，业务文件仍由文件服务提供。同源请求默认使用相对路径；开发环境使用 Vite proxy。原 API 路径、Authorization、Range、追踪头与错误状态需正确转发，HTTPS 终止后的后端协议识别也要在部署验收时核对。

SPA 回退仅处理页面导航：/api、/files 和不存在的资源不能落入 index.html。带内容 hash 的资源可长期缓存；入口 HTML 不长缓存，更新和回退保留匹配的一组入口/资源。Windows/Linux 的配置是部署适配，业务 TS 源码不建立平台分支。

公开配置首期以同源相对路径和构建时变量为主；只在需要同一产物跨不同 API 地址部署时增加启动时公开配置文件。VITE_* 会进入浏览器包，不放签名密钥或连接字符串。[Vite 环境变量说明](https://vite.dev/guide/env-and-mode)

## 10. 后续骨架任务的完成标准

后续实施应先跑官方最小模板，再逐项接入选定依赖，记录准确 CLI/模板与版本。先落地根入口、工作区、providers、API 生成接线和验证配置。用两个最小可编辑测试页签验证保留/关闭/保存生命周期后再接产品；测试页不扩建成假业务系统或大量未来模块页面。

| 验收 | 必须证明 |
| --- | --- |
| 安装与类型生成 | npm ci 可重复；框架 typegen 与 tsc 通过；版本和模板来源可追溯 |
| 静态构建 | ssr:false 真实构建成功；构建不读取身份/业务库；发布不要求 Node 服务 |
| UI 接线 | Ant 中文 locale、主题和提示可用；无第二套 UI 主线；初始 hydration 无错误 |
| 路由与加载 | 登录、工作区、404 与直接深链接可用；业务页签显式拆包；隐藏页不会随活动 URL 换实体 |
| 工作区 | 两个编辑页签切换保留状态；同实体去重、新建身份稳定；关闭其他/全部遵守每个页签保护 |
| 隐藏生命周期 | Ant Tabs + Activity 实测；隐藏暂停展示副作用；控制器、关闭保护及原页签保存回执保持有效 |
| 会话与授权 | 401 冻结工作区；同用户重认证后重新核权；不同用户清旧草稿/缓存；已打开页签也受权限变化约束 |
| 请求接线 | 已验收 DTO 和 Fetch 可生成、类型检查，signal/状态联合类型保留；未连接真实库也如实注明 |
| 边界 | import 规则能发现反向依赖；shared 不导入业务 feature；没有手改 generated |
| 编辑安全基础 | 需要编辑页时先验证草稿与快照隔离、未保存导航和失败保留；不以空 hook 假称完成 |
| 跨平台 | Node 脚本不绑定 PowerShell/cmd；核对大小写、UTF-8、路径；Linux 构建需实际环境验证 |
| 独立发布 | Web 可从本目录构建；Windows/Linux 页面回退与 API 路由区别明确；不修改既有 API 网站去托管 React |

<a id="web-rule-checks"></a>

### 10.1 规则与证据必须对应

以下为持续检查要求；已有配置及历史结果见工程验收记录，每次交付仍需证明当前整合版本的适用场景。

| 规则 | 必须关联的检查/测试 | 通过标准 |
| --- | --- | --- |
| 应用 → feature → shared；跨模块公共意图 | ESLint import 边界，加禁止引用样例核验 | shared 反向引用和 feature 深层跨模块引用确实报错 |
| generated 唯一来源 | 锁文件安装后重新生成并检查 diff | 无未解释的手工生成差异，不回写接受漂移 |
| WEB-EDIT-01/02 字段唯一、dirty 比较 | Form/集合及单元格缓冲测试 | 恢复原值/取消编辑解除对应保护；无多份业务当前值，聚焦/touched 不算修改 |
| WEB-TAB-01/公共契约 切换与输入保护 | 两个编辑标签浏览器测试 | 首次未失焦输入阻止当前/全部关闭；切换无强制校验，隐藏后仍保护；数据替换/离开同用摘要 |
| WEB-TAB-02 保存入口与冻结 | 活动单元格、隐藏 Form 保存浏览器测试 | 最后一个未失焦值进入请求；无效/未完成输入保留且校验失败不发请求；保存准备/保存中本页只读 |
| WEB-TAB-03 回执归原标签 | 两标签并行操作测试 | A 保存后切 B，回执只更新 A，不覆盖 B |
| WEB-TAB-02/03、WEB-API-01 提交分类 | 按端点验证未发送、短路拒绝、无法确认的业务失败、响应丢失、完整成功及已有行级部分成功 | 依据完整证据接纳/解锁；显示业务拒绝不等于确认未生效；部分行回执不被抹掉 |
| WEB-TAB-04 未知恢复与保全 | 恢复/导出/人工终止浏览器矩阵 | 未知不重发/接纳/普通放弃；导出取消/失败、未确认、无权限均不终止，保全后明确确认才释放；批量关闭不绕过 |
| 权限影响已打开页与候选 | 会话/权限浏览器矩阵 | 活动/隐藏页权限撤销生效，缺候选权限不扩权 |
| 根页面构建与联合生命周期 | Framework SPA 构建、Ant Tabs + Activity 实测 | 构建不读身份/API；实际挂载与 Effect 清理符合设计 |

WEB-TAB 规则的正文只在 [多标签设计](Web多标签工作区设计.md) 维护；后续工作流与 Skills 引用规则和测试，不再复制一套不同表述。

导航阻断依照 Router 官方 API 实施，浏览器关闭/刷新另用 beforeunload；两者不能互相替代。[React Router 导航阻断](https://reactrouter.com/how-to/navigation-blocking)

建议后续先落地这套骨架，再实施登录和产品 feature。普通表格/表单优先直接用 Ant；SwiftDataGrid 和通用 editing 按产品真实编辑需求建立最小实现，随后用分类树和单据场景检验扩展，不先造完整 Excel 引擎。

## 11. 开工前待定的产品选择与本轮证据

多标签已明确为首期需求。12 小时内刷新与关闭浏览器重开恢复登录按 [公共设施基线](#web-common-services) 实施；未保存草稿仍只保留在当前浏览器运行会话，跨刷新草稿、离线缓存及服务器 JWT 撤销不在本期。

本轮完成官方资料检索、脚手架静态比较、现有选型/契约工具核对和目录职责规划。未执行 create-react-router/create-vite、npm 安装、TypeScript/React 构建、浏览器测试、Linux 构建或部署；所有框架联合兼容性和目录实例均为后续验收目标，不是已通过证据。

2026-10-08 收口仅修改三份目标文档，保留历史验收结果。2026-10-09 按用户授权同步旧规划及治理/Skills 接线；未新增前后端业务功能，未重跑历史业务验收。

<a id="web-authority"></a>

### 11.1 权威、工作流和 Skills 的分工

| 材料 | 唯一职责 |
| --- | --- |
| 架构决策 | 技术选择、依赖/字段所有权、调用层、规则正文；本文为总入口 |
| 多标签设计 | 身份、活动状态、编辑会话、保存/关闭公共契约及恢复协调 |
| 契约验收记录 | 指定日期与源码范围的后端事实/证据、未验证项；不证明 Web 已实现 |
| AGENTS.md 与 Router | 启动、角色、读取接线、授权边界；不复制架构正文 |
| Skill | 某类任务怎么做、复用什么、如何验收；引用规则正文 |
| 工作流与自动检查 | 执行顺序、文件归属、最终整合验证及规则对应检查 |

规则正文只维护一处。原六份资料继续提供领域细则、历史盘点和规划；本轮新增的字段所有权/调用层以本文为准，页签规则以多标签设计为准。旧 SOP/Skills 分类已收敛为正式入口索引，具体执行只维护在 [Web 工作流](../.agents/workflows/Web前端工作流.md) 与对应 SKILL，不创建另一套规则。

保持 **1 个入口 + 5 个执行 Skill**。正式正文见 [技能组入口](../.codex/skills/react-web/index/SKILL.md)，具体目录及职责映射见 [Skills 接线](web-skills-plan.md)；下表保留已确认职责，不另维护执行步骤。

| Skill | 覆盖范围 | 必须引用 |
| --- | --- | --- |
| Web 任务入口 | 意图/对象判定、架构读取、必要 Skill 选择 | 根 AGENTS/Router、本文；不自动创建或委派任务 |
| 业务模块开发 | feature、页面注册、Form、权限消费、跨模块调用 | §6/7、多标签公共契约与权限设计 |
| API 契约接入 | schema/Orval、请求边界、Query Key、认证与错误 | §8、真实接口验收与契约细则 |
| 编辑与批量保存 | baseline/dirty、Form/Grid、冻结、逐类回执接纳 | §7.1、编辑细则及多标签保存规则 |
| 多标签工作区 | 身份、保活、活动状态、关闭、会话恢复 | 多标签设计；不创建控制器继承框架 |
| 验证与交付 | 类型/依赖、测试、浏览器、构建和证据 | §10.1、测试与验收总纲、最终整合门禁 |

每个正式 Skill 必须有 name/description 元数据、适用/不适用、输入、授权写入范围、必须复用入口、禁止事项及验收步骤；使用 skill-creator 校验。规划文件不是已发现的 Skill。[官方 Skills 说明](https://learn.chatgpt.com/docs/build-skills)

**根目录可达门禁**：根 AGENTS/Router 已接入 Web 角色/工作流/技能正文，并提供仓库根的发现目录链接。本轮只校验接线与可达性；从根目录及实际 Web 子目录启动新会话的发现行为仍须单独验证，WPF 请求继续走既有路线。尚未创建 Web 目录，不能宣称子目录启动已通过。[官方 AGENTS 加载范围](https://learn.chatgpt.com/docs/agent-configuration/agents-md)

当前 Web 技能正文仍在 .codex/skills/react-web；仓库根 .agents/skills 的六个相对目录链接指向单一正文，保持唯一 Skill name，不维护副本、不改全局配置。官方原生发现扫描 .agents/skills 且支持技能目录链接；本轮文件/链接校验与新会话实际发现须区分，具体接线见 [Skills 目录](web-skills-plan.md)。

<a id="web-change-control"></a>

### 11.2 架构变更与并行写入门禁

业务任务不得自行换 Router 模式、增加状态管理库、创建第二套 API Client、改变草稿所有权或扩大后端权限。需要改变时提交独立架构变更说明：具体失败证据、原因、选项、影响范围/调用方、兼容方式和验证计划，经用户确认后修改对应权威规则；不能把单页绕行作为新默认。

并行任务采用隔离工作区或明确逐文件写入归属。共享文件同一时刻只有一个写者，接管须确认原任务停止并重读状态/diff。交付前验证最终整合版本，记录提交/工作树范围和命令结果；隔离快照通过不替代整合通过，失败不得删断言或扩大 skip。此要求沿用现有 [共享工作区治理](../.agents/references/架构治理与路由判定规则.md)。

定稿顺序为：三份讨论稿规则收敛 → 按授权接入 AGENTS/工作流/Skills → 工程基座与联合兼容性验收 → 锁定实际版本/模板基线 → 业务模块开发。骨架验证页只验证既定规则；验证失败走架构变更，不让每个业务页另造机制。
