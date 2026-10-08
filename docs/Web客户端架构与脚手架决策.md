# Web 客户端架构与脚手架决策（方案基线，未实施）

更新：2026-10-08。用户本轮选择“先完成架构、脚手架比较和目录方案”，并明确首期采用 ERP 内部多标签工作区。本文只形成方案，不生成工程、不安装依赖、不修改业务代码，也不启动新的实施任务。

## 1. 建议与边界

建议新建独立 `SwiftExport.WebApp`，采用 **React Router 官方脚手架 + Framework SPA Mode + React + TypeScript + Ant Design + TanStack Query + Orval Fetch**。React Router Framework Mode 的构建仍基于 Vite。项目独立安装、构建、测试和发布；后端是现有 ASP.NET Core HTTP API，开发断言绑定 PostgreSQL + Dapper。

这是针对本项目的方案选择，不是官方规定的 ERP 标准。早期 Data Mode 草案统一调整为 **Framework SPA Mode**，采用官方工程约定和路由类型生成；业务页签在工作区独立懒加载。编辑集合、权限权威和业务命令边界继续保留。接口行为以 [最新接口验收](Web首批接口契约验收.md) 为准，当前产品过期版本是 `422 / BUSINESS_BLOCKED`。相关六份文档已同步，方案不自动安装成 Agent/Skill 规则。

官方文档说明 React Router 的模式区别及 Framework Mode 的能力；React 官方的新项目指南也将 React Router 列为候选框架。采用 Framework Mode 不要求把 .NET 后端替换成 JavaScript 后端。[React 新项目指南](https://react.dev/learn/creating-a-react-app)、[React Router 模式选择](https://reactrouter.com/start/modes)

可预先稳定的是依赖方向、状态所有权、协议入口和发布边界。多文档页签现在就是首期需求；复杂跨页编辑等未来需求仍应按真实用例演进，不能承诺完全不重构。

| 事项 | 本轮形成的方案基线 | 详细权威入口 |
| --- | --- | --- |
| 独立工程与脚手架 | 同仓库独立 SwiftExport.WebApp；官方 React Router 基础模板，Framework SPA | 本文 |
| ERP 多标签工作区 | 内部页签保留草稿；稳定 tabId；关闭协调；隐藏不丢状态 | [多标签设计](Web多标签工作区设计.md) |
| 表格与编辑 | Ant Table + 受控 SwiftDataGrid；纯编辑集合；feature 专用请求映射 | [编辑基础设施](web-frontend-architecture.md) |
| API 契约 | 已验收 Orval Fetch；按实际 HTTP/内层结果消费，不强制改成 409 | [差距状态](web-api-gap-analysis.md)、[验收报告](Web首批接口契约验收.md) |
| 权限 | me.Permissions + 唯一 PermissionKey 目录；已打开页签也重新核权 | [权限设计](web-permission-design.md) |
| 开发流程与技能 | 独立 Web SOP；1 个技能组入口 + 5 个执行 Skill，尚未注册/创建 | [SOP](web-agent-workflow.md)、[Skills 规划](web-skills-plan.md) |
| 发布 | 独立静态产物；Windows/IIS、Linux/Nginx 适配，同源 API 代理 | 本文第 9 节 |

## 2. 脚手架比较

| 候选 | 能提供的基础 | 对本项目的代价 | 判断 |
| --- | --- | --- | --- |
| React Router 官方基础模板，Framework SPA Mode | 标准根路由/路由模块、路由类型生成、按路由拆分和静态 SPA 构建 | 仍须组织业务模块、API、权限与编辑状态；构建时根页面必须可预渲染 | **首选**：采用框架维护的工程约定，按下文补 ERP 边界 |
| Vite 官方 `react-ts` + React Router Data Mode | React/TS/Vite 基础工程；路由数据 API 可按需接入 | 路由装配、类型约束与懒加载更多由项目组织 | **有效备选**：构建时根渲染确实造成难以消除的集成问题时采用；保留同一业务目录边界 |
| Ant Design Pro / Umi Max | 管理台模板、布局、菜单及 Umi 约定，可生成静态前端 | 改变既定 Vite/React Router 工程主线；模板有额外页面、插件及另一套 API 生成接线要裁剪 | **不作本次基座**：若用户更重视现成管理台页面，才重新评估；不是技术不兼容 |
| 社区完整 boilerplate，如 Bulletproof React | 可参考按 feature 聚合和单向依赖的经验 | 同时承接作者的依赖、样例业务和维护策略，无法替代本项目契约验收 | **用作组织参考**，不整体复制源码 |

依据：[React Router 官方安装与模板](https://reactrouter.com/start/framework/installation)、[Vite 官方模板](https://vite.dev/guide/)、[Ant Design 与 Umi 集成](https://ant.design/docs/react/use-with-umi/)、[Ant Design Pro 官方依赖清单](https://github.com/ant-design/ant-design-pro/blob/master/package.json)。当前 Pro 上游已包含 React 19、Ant Design 6、Query 和 Umi Max；不能用“只支持旧版本”否定它。这里没有安装或实测任何候选，优劣是结合当前项目约束的推断。

Ant Design 是组件库，Ant Design Pro 是管理台方案，两者不能混为一谈。选择 Ant Design 组件并不要求使用 Pro/Umi，也不要求为 ProTable 的数据模式改造现有 POST 查询或 BatchSave。

## 3. 采用框架约定，但不引入前端业务服务器

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

版本基线复用已验收工具的 Node 24、Orval 8.40.0/TypeScript 7.0.2 作为兼容候选，不代表它们与尚未生成的框架模板已联合验证。模板可能要求其他精确版本；必须实际安装、类型生成、测试和构建后才能冻结，不能先同时升级全部库。

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
      关闭协调.ts                # dirty/pending/unknown 的关闭保护
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

目录设计参考了社区 [Bulletproof React 的 feature 聚合与单向依赖](https://github.com/alan2207/bulletproof-react/blob/master/docs/project-structure.md)。它是社区工程经验，不是 React 官方标准，也不整体复制其依赖。

另核对了 [Feature-Sliced Design 的层与导入约束](https://feature-sliced.design/docs/reference/layers)。FSD 明确允许仅采用需要的层；本项目借鉴职责和依赖思想，**不宣称完全采用 FSD**，也不照搬 pages/widgets/features/entities 的完整分层。这里的 feature 是 ERP 用例模块聚合，语义与 FSD 的交互切片不完全相同。

实施阶段用 ESLint 约束真实边界：shared 不得导入 features/routes/bootstrap；业务模块不得跨模块导入内部文件；框架 route 模块不得包含领域保存编排。按真实文件检查，不能只写规范。模块公开入口仅在需要时提供窄导出，避免一个总 index 将所有页面提前导入、破坏按路由拆分。

## 7. 状态、请求和路由的所有权

| 状态 | 所有者 | 约束 |
| --- | --- | --- |
| 已登录身份与 token 生命周期 | bootstrap + shared/session | 登录流程由 auth feature 发起；身份切换取消旧请求并清理对应缓存/草稿 |
| me 与权限快照 | Query / 会话初始化 | 权限来自 me，不根据角色显示名猜测；后端最终授权 |
| 产品、字典、候选查询结果 | Query | 同一操作/输入使用规范查询键，允许共享快照；按身份隔离 |
| 页签顺序、激活与描述 | workspace reducer + Context | 不存产品 DTO/表单草稿；只接收 dirty/pending/unknown 摘要与关闭接口 |
| 可分享的活动页签身份 | URL + workspace 地址解析 | 仅活动页签写 URL；隐藏页依照自己的 descriptor，不读取全局 route params |
| 页码与筛选 | 各页签的 feature 状态 | 切换页签保留；dirty 时不能静默切换该页签的结果集 |
| 未保存输入、baseline Version、增改删集合 | feature 编辑状态 / 通用 editing | 只接纳已确认的服务端回执；不被后台查询刷新覆盖 |
| 行选择、展开、活动单元格 | 页面/控件 React 状态 | 与持久业务事实、服务器权限分离 |
| 字段输入错误 | Ant Form / feature validator | 提供输入体验；不复制服务器跨业务状态决策 |

React 官方说明了避免冗余、重复和过深状态的原则；这里把互斥增改删集合从同一编辑状态推导，避免维护数套可漂移副本。[React 状态结构](https://react.dev/learn/choosing-the-state-structure)

Router 负责登录/工作区/错误入口及离开工作区的导航阻断；workspace 负责内部页签。Query 负责业务读取和提交，不同时建立 loaderData 和 Query 两套快照。Ant Form 的提交使用 feature mutation，不同时混用 Router action 执行同一次保存。页签控制器、关闭保护和保存状态位于 Activity 外；隐藏仅暂停页面展示副作用，不能丢草稿或取消已派发写入。具体生命周期见 [多标签设计](Web多标签工作区设计.md)。

routes.ts 只注册框架入口。workspace 页面目录统一 pageKind、权限、业务身份与显式 lazy import；导航目录引用 pageKind，活动地址编解码也查询同一页面目录。业务页不各建一个 URL 路由管理器，框架按路由拆分不能替代内部业务页签的显式懒加载。

**首期必须支持内部多标签工作区**：切换保留筛选、表单、编辑集合及滚动；同一已有单据重复打开激活原页签；新建单据各有独立临时身份；tabId 保存后仍稳定。关闭脏页提供保存/放弃/取消；保存中或结果未知阻止普通关闭；切换不弹未保存确认。浏览器刷新不等于内部切换，跨刷新恢复草稿尚未纳入首期。

## 8. API 接入不会要求页面层重写

已验收工具仍位于 [_tools/Web契约验证](../_tools/Web契约验证/使用说明.md)。该目录用于后端契约验收，不变成前端运行时依赖。Web 项目消费已审核的 schema 快照，持有自己的生成配置、lockfile 和 generated 产物；可从独立项目目录生成、构建，不依赖后端源码或 PowerShell 脚本在场。

后端改动 → 契约验收并导出 schema → 审核兼容差异 → 更新 Web 的 openapi 快照 → 生成 DTO/操作 → 类型检查 → feature mapper/query 调整 → 页面验证。HTTP API 是契约；数据库模型或 C# Contracts 项目不是浏览器依赖。

generated 管线路径只有一个；不同时引入 Orval 和另一套手写 client。统一请求边界可以通过生成器 mutator 或薄调用接线实现，以实际生成验收确定方式；它负责 Authorization、signal、错误分类及 trace，不包含产品规则。传输层不依赖 React hook，也不自行跳转页面；会话层消费认证失效事件并组织重新登录。

必须保留本轮已经验证的事实：login/me 是裸 DTO，其他首批成功多为 ServiceResult；Fetch 状态联合类型不能把非 2xx 当成功。Product 过期版本是 422，而 503/OUTCOME_UNKNOWN 要保留提交意图。通用 CRUD 的外层成功与行失败不能被统一 transport 抹掉。写操作不开自动重试；Query 的默认读取重试/焦点刷新也需显式约束。[Query 默认行为](https://tanstack.com/query/latest/docs/framework/react/guides/important-defaults)、[现有验收报告](Web首批接口契约验收.md)、[Orval 输出配置](https://orval.dev/docs/reference/configuration/output/)

权限键首期仅集中使用真实已核对键，后续机器投影从后端权威目录来；不能从 DTO 的普通 string 字段推导完整权限目录。生成 DTO 的日期字符串不是时区决策；金额 JSON number 不代表前端浮点计算可作为财务权威。编辑模型与 mapper 负责输入边界，服务器决定业务结果。

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

导航阻断依照 Router 官方 API 实施，浏览器关闭/刷新另用 beforeunload；两者不能互相替代。[React Router 导航阻断](https://reactrouter.com/how-to/navigation-blocking)

建议后续先落地这套骨架，再实施登录和产品 feature。普通表格/表单优先直接用 Ant；SwiftDataGrid 和通用 editing 按产品真实编辑需求建立最小实现，随后用分类树和单据场景检验扩展，不先造完整 Excel 引擎。

## 11. 开工前待定的产品选择与本轮证据

多标签已明确为首期需求。会话先采用内存 Bearer、刷新重新登录；未保存草稿先只保留在当前浏览器运行会话。若后续要求刷新保持登录或跨刷新恢复草稿，再单独确定存储、过期、身份隔离与清理策略；不能默默用 localStorage 实现所有持久化。

本轮完成官方资料检索、脚手架静态比较、现有选型/契约工具核对和目录职责规划。未执行 create-react-router/create-vite、npm 安装、TypeScript/React 构建、浏览器测试、Linux 构建或部署；所有框架联合兼容性和目录实例均为后续验收目标，不是已通过证据。

本轮新增本文与多标签设计，并同步原六份文档；可用性盘点保留为历史静态快照。已有 WPF/VSTO/BOM/OpenAPI 并行改动保持原样；没有新增前后端业务功能、Agent/Skill 或数据库写入。
