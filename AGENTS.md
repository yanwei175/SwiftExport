# SwiftExport ERP Codex Agent Rules

本仓库使用类似 Karpathy LLM Wiki 的方式维护项目 AI 工作流：`raw/` 保存未经整理的来源资料，`wiki/` 保存整理后的正式项目知识、skill、workflow、索引和日志，`AGENTS.md` 只规定 Codex 的行为边界和执行流程。

## 基本原则

1. 每次任务先读取 `wiki/index.md`，再根据索引进入对应 `wiki/skills/**`、`wiki/workflows/**`、`wiki/project/**` 或源码文件。
2. 源码事实优先于 `wiki/` 和 `raw/`；如果发现冲突，不要静默覆盖，应在 `wiki/log.md` 记录“可能过期”。
3. `AGENTS.md` 是最高优先级行为规则；`wiki/skills/**` 和 `wiki/workflows/**` 是任务级细则。
4. 先读项目上下文，再改代码；不要凭记忆重建项目结构。
5. 不修改业务代码，除非用户明确要求实现功能、修复问题或调整业务行为。
6. 不删除用户已有改动；遇到非本次任务产生的变更，应保留并绕开。
7. 所有新增注释、规范说明、文档说明优先使用中文。
8. 涉及业务流程、需求、采购、入库、出库、PackingList、客供件或状态回写的任务，必须先读取 `wiki/project/erp-business-blueprint.md`。

## AI Wiki 三层结构

### raw：原始资料层

`raw/` 保存未经整理的来源资料、草稿、用户手动输入、对话记录、截图和外部文档摘录。

建议入口：

- `raw/inbox/skills/**`：用户手动新增的 skill 草稿。
- `raw/inbox/workflows/**`：用户手动新增的 workflow 草稿。
- `raw/inbox/project/**`：项目资料草稿。
- `raw/articles/**`、`raw/chat-logs/**`、`raw/screenshots/**`、`raw/specs/**`：外部资料、对话、截图、规格说明等来源。

`raw/` 默认只读。不要自动删除、移动或重写 raw 原文，除非用户明确要求。

### wiki：正式知识层

`wiki/` 是 AI 维护的正式知识库，保存整理后的项目知识、skill、workflow、项目地图、索引和日志。

`wiki/index.md` 是 Codex 查询入口。任务开始时先读它，用它判断接下来应该读哪些 wiki 页面和源码。

`wiki/log.md` 是 AI 工作流日志。以下情况应更新：

- 新增、迁移或调整 skill / workflow。
- 项目地图、入口规则或架构边界发生变化。
- 发现 `raw/`、`wiki/` 与源码事实冲突。
- 完成一次重要知识维护。

### AGENTS：行为规则层

`AGENTS.md` 规定 Codex 如何行动、如何选择上下文、哪些目录可以写、哪些风险必须提示。

## 默认工作流

1. 读取 `wiki/index.md`。
2. 按 `wiki/index.md` 的任务路由读取相关 `wiki/skills/**`、`wiki/workflows/**` 或 `wiki/project/**`。
3. 读取源码中的真实实现，确认当前模式。
4. 制定小范围实现方案。
5. 修改文件时贴合现有分层、命名、DI、DTO、事务和前端 MVVM 模式。
6. 根据修改范围运行合适的构建或检查；如未运行，最终回复说明原因。
7. 如本次改动影响 AI 工作流或长期项目知识，更新 `wiki/index.md` 和 `wiki/log.md`。

## AI 资产 Ingest 工作流

当用户手动新增 raw 来源，并要求“ingest”“更新索引”“整理工作流”“纳入 AI Wiki”时，按本流程处理。具体资产分类、任务路由和当前条目列表维护在 `wiki/index.md`，不要因为新增 skill 或 workflow 就修改 `AGENTS.md`。

Ingest 步骤：

1. 先读取 `wiki/index.md`，确认当前已有索引分类和命名方式。
2. 读取用户指定的 raw 文件或目录；如果用户只说“新增了 skill / workflow”，先用文件搜索确认新增路径。
3. 判断来源类型：skill 草稿、workflow 草稿、project doc、风险记录、对话、截图、规格说明或其他。
4. 提取可长期复用的规则、事实、流程和风险，整理到对应 `wiki/**` 页面。
5. 如果整理结果新增或修订了正式页面，更新 `wiki/index.md`，加入路径、用途说明和“什么时候读取”。
6. 更新 `wiki/log.md`，记录 ingest 时间、来源路径、更新了哪些 wiki 页面、是否发现冲突。
7. 如发现 `raw/` 与源码事实冲突，优先相信源码，并在 `wiki/log.md` 标记“可能过期”；不要为了让文档一致而修改业务代码。

Ingest 限制：

- 不要把 `raw/` 原文大段复制进 `wiki/`；wiki 页面应总结、归纳、重组并保留来源意识。
- 不要自动删除、移动或重写 raw 文件，除非用户明确要求。
- 不要因为 ingest 文档而修改业务源码。
- 如果 raw 来源中包含可执行开发规则，应整理到 `wiki/skills/**` 或 `wiki/workflows/**`，再由 `wiki/index.md` 索引。

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

职责：Entity、Repository Interface、ReadModel、基础领域接口。

禁止：依赖 Application、Infrastructure、WebApi、WPF View 层。

### Infrastructure 层

路径：`SwiftExport.Infrastructure`

职责：Dapper Repository 实现、`RepositoryBaseContext`、`DbSession`、SQL Server 访问、审计字段打戳。

禁止：依赖 Application、调用 Controller、处理 UI 状态、硬编码连接字符串。

### Contracts 层

路径：`SwiftExport.Contracts`

职责：DTO、Refit API Interface、枚举、通用响应包装、前后端共享契约。

规则：Controller 路由与 Refit 接口保持一致；前后端传输模型优先放 DTO；只读查询如果已有合适 DTO，可以直接使用 DTO，不必强行新增 Entity。

## 写入边界

默认只写入与用户任务直接相关的文件。不要顺手重构无关模块。

前端任务默认写入范围：

- `SwiftExport.View.FW/**`
- `SwiftExport.VSTO/**`
- `SwiftExport.Contracts/**` 中前端依赖的 API、DTO、枚举、通用契约

前端任务只读辅助范围：

- `SwiftExport.Core/**` 中 Entity、ReadModel、接口定义

前端任务禁止借机写入：

- `SwiftExport.WebApi/**`
- `SwiftExport.Application/**`
- `SwiftExport.Infrastructure/**`
- `SwiftExport.Core/**`

后端任务必须按分层需要修改，不能跳层实现。

## 常用命令

后端构建：

```powershell
dotnet build .\SwiftExport-WebAPI.slnx
```

后端运行：

```powershell
dotnet run --project .\SwiftExport.WebApi\SwiftExport.WebApi.csproj --launch-profile https
```

完整解决方案构建：

```powershell
msbuild .\SwiftExport.slnx /p:Configuration=Debug /p:Platform=AnyCPU
```

数据库种子工具：

```powershell
dotnet run --project .\_tools\DbSeed\DbSeed.csproj
```

## 最终回复格式

完成任务后用中文简洁汇报：

- 改了哪些文件。
- 是否修改业务代码。
- 是否运行构建/测试。
- 如未运行，说明原因。
- 如发现连接字符串、编码或未提交改动风险，简要提示。
