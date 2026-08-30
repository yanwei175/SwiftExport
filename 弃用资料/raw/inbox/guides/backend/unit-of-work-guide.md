# Unit of Work Guide

## 适用场景

适用于多个 Repository 需要共享连接和事务的保存、批量处理、主从表提交、跨表一致性操作。默认 WebAPI 后端优先采用 scoped `DbSession`；通用或跨架构场景再采用 UOW。

## 推荐做法

- 默认 WebAPI 路线：`HTTP Request Scope -> DbSession -> ServiceBaseContext -> Service -> RepositoryBaseContext -> Repository`。
- `DbSession` 注册为 scoped，同一次 HTTP request 内 Service 和 Repository 应拿到同一个实例。
- Service 层通过当前 `DbSession` 控制事务边界，Repository 只使用当前连接和事务。
- 通用路线：UOW 持有当前连接和事务，Repository 使用 UOW 提供的连接/事务执行 SQL。
- 未提交的 UOW 在释放时应自动回滚。
- provider 无关接口只暴露通用数据库抽象，例如 `DbConnection`、`DbTransaction`。
- 连接字符串读取应通过标准配置或项目既有配置，不让连接工厂依赖 UI 或应用服务。
- DI 注册应保持生命周期清晰，`DbSession` 通常为 scoped，UOW 不应是 Singleton。
- 默认不抽 `IDbSession`；只有多 provider、读写分离、多租户、测试替身或严格分层需要时再考虑接口。

## 禁止事项

- 禁止 Repository 自己开启业务事务。
- 禁止 ViewModel 或 Controller 手动 new 连接工厂和事务对象。
- 禁止连接工厂依赖 `AppConfigurationService`、文件选择器或 UI 状态。
- 禁止接口返回 provider 专属连接类型作为公共抽象。
- 禁止在重构 UOW 时顺手修改业务表结构。
- 禁止让 Service 和 Repository 使用不同 scoped session 导致事务断裂。
- 禁止为了“看起来抽象”机械创建 `IDbSession`。

## Codex 修改前检查清单

- 是否已有 UOW、DbSession、连接工厂或事务封装。
- 当前项目默认使用 scoped `DbSession`，还是 UOW/连接工厂。
- `DbSession`、`ServiceBaseContext`、`RepositoryBaseContext` 是否都注册为 scoped。
- 当前数据库 provider 和连接字符串来源是否已确认。
- Repository 方法是否已经能接收或访问当前事务。
- 事务提交、回滚、释放和异常路径是否清晰。
- 有 `.csproj` 时，修改后是否可运行 `dotnet build`。

## 常见错误

- 每个 Repository 各自打开连接，导致同一用例无法统一回滚。
- UOW 生命周期过长，跨请求或跨页面持有连接。
- 忘记在异常路径回滚。
- 连接工厂和运行时配置写入服务互相耦合。
- 把 UOW 包装成全局状态。
- WebAPI 中把 `DbSession` 注册成 Singleton 或 Transient，破坏 request scope 语义。
