# SwiftExport ERP Codex Agent Rules

本文件只保存稳定铁律。任务路由、skill、workflow、项目资料入口统一由 `wiki/index.md` 管理。

## 基本原则

1. 每次任务先读 `wiki/index.md`，只按短路由读取当前任务需要的最小 wiki 文件。
2. 再读取源码中的真实实现；源码事实优先于 `wiki/` 和 `raw/`。
3. 如果 wiki/raw 与源码冲突，不要静默覆盖；知识维护任务应在 `wiki/log.md` 记录“可能过期”。
4. `AGENTS.md` 是最高优先级行为规则；`wiki/workflows/**`、`wiki/skills/**`、`wiki/guides/**` 是任务级细则。
5. 不修改业务代码，除非用户明确要求实现功能、修复问题或调整业务行为。
6. 不删除用户已有改动；遇到非本次任务产生的变更，应保留并绕开。
7. 所有新增注释、规范说明、文档说明优先使用中文。
8. 默认只写入与用户任务直接相关的文件，不顺手重构无关模块。

## 当前主线

- 当前前端主线：`.NET 10 WPF + CommunityToolkit MVVM`。
- 旧 `.NET Framework 4.8 + MVVMLight + VSTO` 只在维护旧前端时读取对应兼容文档。
- `raw/` 不参与日常任务路由；只有用户明确要求 ingest、整理来源或指定 raw 文件时才读取。

## 默认工作流

1. 读 `wiki/index.md`。
2. 按短路由读取最小 wiki 文件。
3. 搜索并读取相关源码，确认当前实现。
4. 制定小范围方案并执行。
5. 按改动范围运行合适的构建或检查；未运行时说明原因。
6. 如改动影响长期规则或 AI Wiki，更新 `wiki/index.md`、`wiki/catalog.md` 或 `wiki/log.md`。

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
