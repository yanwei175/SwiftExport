# SwiftExport ERP Codex Agent Rules

本项目后续以 `AGENTS.md` + `.agents/**` + `.codex/skills/**` 作为 AI 开发入口，不再把 `wiki/**` 作为默认工作流来源。`wiki/**` 仅作为旧资料归档，除非用户明确要求读取。

## 启动顺序

1. 先读本文件。
2. 再读 `.agents/router.toml`，按任务类型选择子 agent 和工作流。
3. 再读命中的 `.agents/*.toml` 和 `.agents/workflows/*.md`。
4. 如任务触发 skill，优先读对应 `.codex/skills/**/SKILL.md`；根目录 `skills/**` 仅在后续明确建立时使用。
5. 最后读取真实源码；源码事实优先于所有文档。

## 基本铁律

1. 不修改业务代码，除非用户明确要求实现功能、修复问题或调整业务行为。
2. 不删除用户已有改动；遇到非本次任务产生的变更，应保留并绕开。
3. 默认只写入与用户任务直接相关的文件，不顺手重构无关模块。
4. 所有新增注释、规范说明、开发文档优先使用中文。
5. 文件名能用中文时优先中文；Codex/工具强制识别的文件名除外，例如 `AGENTS.md`、`SKILL.md`、`*.toml`。
6. 如果文档与源码冲突，优先相信源码，并在最终回复中提示文档可能过期。
7. 当前前端主线是 `.NET 10 WPF + CommunityToolkit MVVM`。
8. 当前 .NET 10 WPF 的 MVVM 代码必须遵循 `.codex/skills/wpf-dotnet10-design/communitytoolkit-mvvm/SKILL.md`。

## 子 Agent 与路由

- 子 agent 定义放在 `.agents/*.toml`。
- 路由定义放在 `.agents/router.toml`，不要把不断变化的路由写进本文件。
- 可复用工作流放在 `.agents/workflows/*.md`。
- 子 agent 只写角色、边界、输入输出和必须遵守的规则；详细步骤放到 workflow。
- 一个任务只选择一个主 agent；需要时由主 agent 在计划中点名需要哪些辅助 agent 视角。

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

禁止：直接创建 `SqlConnection`、硬编码连接字符串、放入 WPF UI 状态或弹窗逻辑。

### Core 层

路径：`SwiftExport.Core`

职责：Entity、Repository Interface、基础领域接口。

禁止：依赖 Application、Infrastructure、WebApi、WPF View 层。

### Infrastructure 层

路径：`SwiftExport.Infrastructure`

职责：Dapper Repository 实现、`RepositoryBaseContext`、`DbSession`、SQL Server 访问、审计字段打戳。

禁止：依赖 Application、调用 Controller、处理 UI 状态、硬编码连接字符串。

### Contracts 层

路径：`SwiftExport.Contracts`

职责：DTO、Refit API Interface、枚举、通用响应包装、前后端共享契约。

规则：Controller 路由与 Refit 接口保持一致；前后端传输模型优先放 DTO；API/前端可见只读联表模型优先放 Contracts DTO。

## 写入边界

当前 .NET 10 前端任务默认写入范围：

- `SwiftExport.Client.Wpf/**`
- `SwiftExport.Contracts/**` 中前端依赖的 API、DTO、枚举、通用契约

旧 WPF/VSTO 兼容维护任务写入范围：

- `SwiftExport.View.FW/**`
- `SwiftExport.VSTO/**`
- `SwiftExport.Contracts/**` 中前端依赖的 API、DTO、枚举、通用契约

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
dotnet run --project .\_tools\DbSeed\DbSeed.csproj
```

## 最终回复格式

完成任务后用中文简洁汇报：

- 改了哪些文件。
- 是否修改业务代码。
- 是否运行构建/测试。
- 如未运行，说明原因。
- 如发现连接字符串、编码或未提交改动风险，简要提示。
