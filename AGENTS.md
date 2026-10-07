# SwiftExport ERP Codex Agent Rules

本项目后续以 `AGENTS.md` + `.agents/**` + `.codex/skills/**` 作为 AI 开发入口。旧 `wiki/**`、`raw/**` 和 `_codex_handoff/**` 已统一迁入 `弃用资料/**`，不作为默认工作流或事实来源，除非用户明确要求读取历史资料。

## 启动顺序

1. 先读本文件。
2. 再读 `.agents/router.toml` 及其 policy 引用的路由判定规则，按任务意图与目标对象选择主 agent 和工作流，不以首个关键词命中决定。
3. 再读命中的 `.agents/*.toml` 和 `.agents/workflows/*.md`。
4. 如任务触发 skill，优先读对应 `.codex/skills/**/SKILL.md`；根目录 `skills/**` 仅在后续明确建立时使用。
5. 最后读取真实源码；源码事实优先于所有文档。

### 读取去重

- 同一任务内，同一路径、同一版本的规则只读取一次；不同入口再次引用时复用已读内容，不重新展开其引用链。引用回到正在读取的文件时停止该分支，继续处理尚未读取且适用的依赖。
- “先读”“必读”“完成前读取”表示所需上下文必须齐备，不表示每个阶段重读。只读过部分章节时补齐本次所需的未读部分；文件变化、任务范围新增或上下文已不可用时，重新读取必要部分。
- Skill 与工作流仍按当前业务任务选择，所有适用的业务、技术和验证要求保持有效；读取去重不构成跳过门禁或测试的理由。

## 基本铁律

### Karpathy-Inspired 编程行为四原则

#### 1. Think Before Coding：先想再写

- 开始修改前先读取命中的 Agent、Workflow、Skill、目标源码、调用方和相关测试。
- 明确列出会影响实现的假设；需求存在多种合理解释时，不得静默选择一种。
- 有明显更简单的方案时主动提出。
- 对业务规则、状态边界或数据含义感到困惑时，停止编码并明确提问。

#### 2. Simplicity First：简洁优先

- 不增加用户没有要求的功能。
- 不为一次性代码提前创建抽象，不增加尚未要求的扩展点、配置项或兼容层。
- 能用简单实现清楚完成时，不采用复杂架构。
- 代码量明显超出问题规模时，重新检查是否过度设计。

#### 3. Surgical Changes：精准修改

- 只修改当前任务必须涉及的代码，不顺手优化相邻代码、注释、命名或格式。
- 不重构没有问题的代码；即使个人偏好不同，也应匹配项目现有风格和分层。
- 只清理由本次修改直接造成的孤立代码。
- 保留用户已有改动，不覆盖或回退无关变更。

#### 4. Goal-Driven Execution：目标驱动

- 将模糊要求转换为可验证的完成目标。
- 多步骤任务在实施前明确计划、验证点和完成标准。
- 修复 Bug 时优先建立可复现条件，再修复并验证问题不再出现。
- 实现过程中围绕成功标准自主闭环，不停留在只写代码未验证的状态。
- 修改后检查实际 diff，并运行与改动范围匹配的最小构建或测试。
- 未验证、验证失败或环境缺失时必须如实说明，不得声称已经完成。

### 项目规则

1. 不修改业务代码，除非用户明确要求实现功能、修复问题或调整业务行为。
2. 源码用于确认实现事实；文档与源码冲突时，区分过期描述和仍有效的目标约定，登记偏差，不把当前实现自动提升为目标规则。
3. 文件名能用中文时优先中文；Codex/工具强制识别的文件名除外，例如 `AGENTS.md`、`SKILL.md`、`*.toml`。
4. 当前前端主线是 `.NET 10 WPF + CommunityToolkit MVVM`。
5. 当前 .NET 10 WPF 的 MVVM 代码必须遵循 `.codex/skills/wpf-dotnet10-design/communitytoolkit-mvvm/SKILL.md`。

### 中文注释铁律

- 除 XAML 外，新增或修改的手写源码必须提供中文职责级注释。
- 类型、接口、枚举及 `public`、`protected` 成员使用中文 XML `<summary>`，说明职责、输入输出和重要副作用。
- 私有非显然方法，以及业务规则、状态流转、事务、复杂 SQL、算法和异常分支，必须有中文方法说明或关键逻辑注释。
- 注释解释“负责什么、为什么这样做、有什么边界”，不得逐行复述语法。
- 修改已有行为时同步更新相关注释，禁止保留已经失真的说明。
- 简单自动属性、显然的一行转发、生成代码和设计器文件不强制机械注释。
- XAML 不强制注释；复杂模板或资源边界确实需要说明时仍使用中文。
- 采用“触碰即补齐”：普通任务只补齐本次涉及的类型、方法和复杂逻辑，不批量修改无关模块。
- 规范说明和开发文档使用中文；必要的英文类型名、协议名和工具固定名称可以保留。
- 中文注释属于任务完成标准，但不能成为违反 Surgical Changes、批量改写无关代码的理由。

## 子 Agent 与路由

- 子 agent 定义放在 `.agents/*.toml`。
- 路由定义放在 `.agents/router.toml`，不要把不断变化的路由写进本文件。
- 可复用工作流放在 `.agents/workflows/*.md`。
- 子 agent 只写角色、边界、输入输出和必须遵守的规则；详细步骤放到 workflow。
- 一个任务只选择一个主 agent；需要时由主 agent 在计划中点名需要哪些辅助 agent 视角。
- 执行前简述主 Agent、Workflow、判定理由和授权写入范围；只读请求不得因角色可写而修改文件。
- 共享文件的写入归属与接管必须遵守 Router 引用的治理规则；角色选择不等于授权创建任务或启动并行 Agent。

## Skills 文档约定

后续开发 `.codex/skills/**` 时遵守：

- 后端技能组放在 `.codex/skills/aspdotnet10/**`。
- 前端 WPF 设计、控件、MVVM 技能组放在 `.codex/skills/wpf-dotnet10-design/**`。
- 新增技能应先接入对应技能组的 `index/SKILL.md`，不要把技能路由写进本文件。

- 技能目录名可以用英文短横线，便于工具识别。
- `SKILL.md` 保持工具要求的固定文件名。
- 给人和 AI 读的说明文档、参考资料、示例代码文件名尽量使用中文。
- 建议结构：

```text
.codex/skills/
  skill-id/
    SKILL.md
    docs/
      使用规则.md
      检查清单.md
    references/
      官方资料摘要.md
    examples/
      产品页面ViewModel示例.cs
```

## 分层边界

### WebApi 层

路径：`SwiftExport.WebApi`

职责：Controller、Middleware、Filter、Web 认证授权、HTTP 请求/响应边界。

禁止：Controller 直接写 SQL、直接依赖 Infrastructure 具体仓储实现、承担复杂业务编排。

### Application 层

路径：`SwiftExport.Application`

职责：业务服务、业务校验、业务流程编排、调用 Repository 接口、控制业务事务边界。

禁止：引用 Infrastructure 或 SqlClient 实现、直接创建数据库连接、硬编码连接字符串、放入 WPF UI 状态或弹窗逻辑。连接与事务通过 Core `IDbSession` 共享，最外层用例仍拥有事务。

### Core 层

路径：`SwiftExport.Core`

职责：Entity、Repository Interface、基础领域接口。

禁止：依赖 Application、Infrastructure、WebApi、WPF View 层。

### Infrastructure 层

路径：`SwiftExport.Infrastructure`

职责：Dapper Repository 实现、`RepositoryBaseContext`、`DbSession`、数据库访问、审计字段打戳。当前数据库运行接线为 PostgreSQL + Dapper；SQL Server 旧实现已退出，历史迁移证据另行保留。

禁止：依赖 Application、调用 Controller、处理 UI 状态、硬编码连接字符串。

### Contracts 层

路径：`SwiftExport.Contracts`

职责：DTO、Refit API Interface、枚举、通用响应包装、前后端共享契约。

规则：Controller 路由与 Refit 接口保持一致；前后端传输模型优先放 DTO；API/前端可见只读联表模型优先放 Contracts DTO。

## 写入边界

### 数据库运行基线（2026-10-07）

- SQL Server 已退出当前运行实现，后续只使用 PostgreSQL + Dapper，不建设长期双库支持或提供者切换框架。
- 数据库相关任务读取 [PostgreSQL 当前基线](.agents/references/PostgreSQL当前基线.md)，再按工作流选择现有技术 Skill；不复制一套旧新双库工作流。
- 当前 Infrastructure DI 已使用 PostgreSQL 连接、会话及对应仓储；不得据旧迁移阶段说明恢复 SQL Server 接线或混用仓储。
- 实现事实见 Infrastructure/DependencyInjection.cs 和真实调用链，历史迁移及验证范围见 docs/postgresql-migration；旧阶段顺序不是当前自动开发待办。源码接线存在不代表所有业务组合或运行库写入已验收。
- 优先复用现有会话、事务、异常分类及元数据；不为迁移新增无真实用例的实体表、通用数据库框架或全局业务锁。BOM 编号可共享，BomVersion 仅表示有效性，不引入业务多版本。
- 新目标不等于任意数据库写入授权：原有隔离测试、备份保护、破坏性操作确认及不得自动部署/推送规则继续有效。
- 旧聊天恢复执行时须重新核对当前入口和源码；历史聊天、迁移清单旧阶段及归档资料不作为当前技术栈或自动待办来源。

D:\DevCaches是NuGet包的文档地址,你拥有权限,避免每次隔离构建都因目录缺少NuGet资产文件而退出.

数据库检查默认只读；写入测试须明确实例、库名及初始化/清理范围，备份或“测试库”说明不等于任意清库授权。当前使用 PostgreSQL + Dapper；结构由实体元数据及PG名称/类型映射生成，生成脚本不等于已部署或并发验收通过。用户此前选择只迁有效基础数据、不迁单据历史；实际迁移及验收证据保留在 docs/postgresql-migration。当前代码职责见 [后端说明](docs/后端整体架构与文件夹职责建议.md)，模块事实见 [业务文档索引](docs/业务文档索引.md)。

当前 .NET 10 前端任务默认写入范围：

- `SwiftExport.Client.Wpf/**`
- `SwiftExport.Client.Wpf.Tests/**` 中当前任务相关的客户端测试、夹具和测试数据
- `SwiftExport.Contracts/**` 中前端依赖的 API、DTO、枚举、通用契约

补测试不允许修改后端业务行为、删除有效断言或绕过失败。EnterpriseGrid 框架及其专项测试由 Grid Architect 按授权负责；普通前端任务可运行专项测试，但不会因此获得其写入权限。

前端任务禁止借机写入：

- `SwiftExport.WebApi/**`
- `SwiftExport.Application/**`
- `SwiftExport.Infrastructure/**`
- `SwiftExport.Core/**`

后端任务必须按分层需要修改，不能跳层实现。

## 常用命令

```powershell
dotnet build .\SwiftExport.Client.Wpf\SwiftExport.Client.Wpf.csproj
dotnet build .\SwiftExport-WebAPI.slnx
msbuild .\SwiftExport.slnx /p:Configuration=Debug /p:Platform=AnyCPU
& .\_tools\DbSeed\同步权限目录.ps1
```

## 最终回复格式

完成任务后用中文简洁汇报：

- 改了哪些文件。
- 是否修改业务代码。
- 是否运行构建/测试。
- 如未运行，说明原因。
- 如发现连接字符串、编码或未提交改动风险，简要提示。
