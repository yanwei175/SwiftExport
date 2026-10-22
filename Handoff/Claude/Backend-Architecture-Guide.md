# SwiftExport 后端架构治理文档（单据类 API 为主）

> 适用范围：ASP.NET Core（.NET 10）后端，向 Web 与 WPF 客户端提供同一套 HTTP API。
> 系统规模：4–5 名用户的小型 ERP。所有设计按此规模取舍：**正确 > 简单易懂 > 可扩展**。
> 本文不以现有代码为前提；现有代码迁移时以本文为目标。

---

## 0. 先说结论（一页版）

| 关注点 | 采用 | 位置 | 不采用 |
|---|---|---|---|
| 分层 | 4 个项目 + 1 个契约项目（Clean Architecture 简化版） | — | 微服务、事件溯源、消息队列 |
| 用例组织 | **按功能文件夹**（Feature Folder），一个用例一个 Service 方法 | Application | MediatR、每个用例一个 Handler 类 |
| 认证 | ASP.NET Core Authentication（JWT Bearer） | Api `Program.cs` | 自写认证中间件 |
| 授权 | **Policy-based Authorization + 权限码**，在 Endpoint 上声明 | Api | 在 Service 里再查一遍权限、权限字典 |
| 输入校验 | DataAnnotations / FluentValidation，自动返回 400 | Api 边界 | 业务代码里校验字符串长度 |
| 业务规则 | 单据实体的方法（`Post()`、`Confirm()`）+ Service 编排 | Domain / Application | Builder/Policy/Support 多层转发 |
| 事务 | 保留 scoped `DbSession` 管连接；增加一个极薄的 `IUnitOfWork` 作为唯一的事务开关（见第 15 节） | Application 声明，Infrastructure 实现 | 多处各自开启/回滚事务、公开可写的 `Transaction` 属性 |
| 并发 | 乐观并发（`Version` 列）+ 必要处 `SELECT … FOR UPDATE` | Infrastructure | Serializable 全局使用、多层 advisory 锁 |
仅正式单据命令使用请求体 `CommandId` + `command_receipt` 回执表
| 错误 | 抛分类异常 → 全局 `IExceptionHandler` → ProblemDetails（RFC 9457） | Api | 每个模块 try/catch 转 ServiceResult |
| 日志 | Serilog + 请求日志 + TraceId；异常只记一次 | Api 边界 | 每层都记同一个异常 |
| 业务审计 | 审计表，与业务同事务写入 | Application | 用技术日志代替审计 |

---

## 1. 项目与依赖方向

```
SwiftExport.Contracts       (netstandard2.0) 请求/响应 DTO、枚举、错误码常量、Refit 接口
		▲             ▲
		│             │（客户端也引用）
SwiftExport.Api  ──► SwiftExport.Application ──► SwiftExport.Domain
		│                     ▲                        ▲
		└──► SwiftExport.Infrastructure ───────────────┘
			 （实现 Application/Domain 声明的接口；Api 仅在 Program.cs 注册它）
```

规则：
1. **Domain** 不引用任何其他项目，不引用 Dapper/ASP.NET。
2. **Application** 只引用 Domain 和 Contracts；用接口（`IUnitOfWork`、`IInboundRepository`）描述它需要的基础设施。
3. **Infrastructure** 实现这些接口（Dapper + Npgsql）。
4. **Api** 只做 HTTP 适配；只在组合根（`Program.cs`/DI 扩展）中引用 Infrastructure。
5. **Contracts** 是对外承诺，目标框架固定为 **netstandard2.0**（WPF、VSTO 共用），修改需考虑 WPF、VSTO、Web 三个客户端的兼容。约束见第 16 节。

> 参考：Microsoft《Common web application architectures》— Clean Architecture
> https://learn.microsoft.com/dotnet/architecture/modern-web-apps-azure/common-web-application-architectures

---

## 2. 一个请求的标准生命周期

```
HTTP 请求
 1. 异常处理中间件（UseExceptionHandler）        —— 兜底，所有异常在这里转 ProblemDetails
 2. 请求日志（UseSerilogRequestLogging）          —— 每个请求一条：方法、路径、状态码、耗时、TraceId
 3. 认证（UseAuthentication）                    —— 解析 JWT，得到 User
 4. 授权（UseAuthorization）                     —— 按 Endpoint 上声明的 Policy 判断，失败 401/403，业务代码根本不执行
 5. 模型绑定 + 输入校验（[ApiController]）        —— 格式错误自动 400
 6. Controller Action                           —— 组装命令对象，调用 Application
 7. Application Service                         —— 开事务 → 幂等检查 → 加载单据(加锁) → 调用领域方法 → 保存 → 审计 → 提交
 8. Infrastructure Repository                   —— SQL
 9. 返回 DTO → 200/201；异常 → 第 1 步统一转换
```

**这就是“命令管道”的全部含义**：一个写请求必须依次经过的环节。在本项目里，管道的 1–5 步由 ASP.NET Core 自带的中间件和过滤器完成；第 7 步的“开事务、幂等、提交”由一个公共组件 `ICommandExecutor` 完成。**不需要**再自己写一个 `CommandPipeline` 类重复做授权、校验、日志。

---

## 3. 纵向：每层做什么 / 不做什么

### 3.1 Api（WebApi）

**做：**
- 路由、HTTP 动词、状态码。
- 声明权限：`[HasPermission(Permissions.Inbound.Post)]`。
（如把路由 `{id}` 与请求体一起交给 Service）
- 返回 DTO；`201 Created`、`204 NoContent` 等语义由 Controller 决定。
- 全局：认证、授权、异常处理、请求日志、CORS、OpenAPI、限流（如需要）。

**不做：**
- 不写 SQL，不开事务，不 try/catch 业务异常。
- 不写业务 if（如“已过账不能修改”）。
- 不再手工查权限（声明式已完成）。

> Controller 可以多于一行：组装参数、选择状态码都是它的职责。限制的是“业务逻辑”，不是“行数”。

### 3.2 Application

**做：**
- 一个用例 = 一个 Service 公共方法（`InboundService.PostAsync`）。
- 决定事务边界（调用 `ICommandExecutor` / `IUnitOfWork`）。
- 编排：加载聚合 → 调用实体方法 → 调用其他领域服务（如库存记账）→ 保存 → 写审计。
- 跨单据的规则（入库数量不能超过采购未收数量）在这里协调。
- 查询服务直接返回 DTO（读模型，可直接走 Dapper 查询，不必经过实体）。

**不做：**
- 不处理 HTTP（不出现 `IActionResult`、状态码）。
- 不检查“用户有没有这个功能权限”（Api 已做）。
- 不把异常转换成返回值（交给全局处理）。
- 不为每个模块写 `XxxSupport.RunAsync` 包装。

### 3.3 Domain（Core）

**做：**
- 实体与单据状态机：`InboundDocument.Post()`、`PurchaseOrder.Confirm()` 内部检查“只有草稿可过账”，并修改状态、过账人、时间。
- 单据自身能判断的规则放实体（状态流转、明细数量 > 0、合计金额）。
- 领域异常类型（`DomainException`）。
- 仓储接口（也可放 Application，二选一，全仓统一）。

**不做：**
- 不访问数据库、不读当前用户、不读时间（时间由参数传入或 `TimeProvider`）。
- 不为了“富模型”把跨单据协调硬塞进实体。

**判断标准：** 只看本单据的数据就能判断的规则 → 实体；需要查别的单据/库存/主数据 → Application Service。

### 3.4 Infrastructure

**做：**
- Dapper 仓储、SQL、`UnitOfWork`（持有 `NpgsqlConnection` + `NpgsqlTransaction`）。
- 乐观并发：`UPDATE … WHERE id=@Id AND version=@Version`，影响行数 0 → 抛 `ConcurrencyException`。
- 需要串行的资源行（库存余额）用 `SELECT … FOR UPDATE`。
- 幂等记录表、审计表的读写实现。
- 当前用户（`ICurrentUser`）基于 `IHttpContextAccessor` 的实现。

**不做：**
- 不做业务判断（不在仓储里判断“能否过账”），SQL 中的状态条件只是并发保护。
- 不自行提交/回滚事务（只有 UnitOfWork 能）。

### 3.5 Contracts

**做：** 请求/响应 DTO、枚举、错误码常量、Refit 接口、权限码常量（供客户端隐藏按钮）。
**不做：** 不放实体、不放业务逻辑、不引用 ASP.NET。

---

## 4. 认证与授权

### 4.1 概念区分
- **认证（Authentication）**：你是谁 → JWT Bearer。
- **授权（Authorization）**：你能不能用这个功能 → 权限码（如 `Inventory.Inbound.Post`）。
- **业务规则**：这张单现在能不能过账 → **不是权限**，属于 Domain/Application，返回 409/422。

本系统只需要“模块-功能-操作”权限，不做数据行级权限。

### 4.2 设计
1. 权限码集中定义（Contracts）：
   ```csharp
   public static class Permissions {
	   public static class Inbound {
		   public const string View = "Inventory.Inbound.View";
		   public const string Edit = "Inventory.Inbound.Edit";
		   public const string Post = "Inventory.Inbound.Post";
	   }
   }
   ```
2. Api 中定义特性 `HasPermissionAttribute : AuthorizeAttribute`，把权限码转成 Policy 名 `perm:Inventory.Inbound.Post`。
3. 自定义 `IAuthorizationPolicyProvider` 按前缀动态生成 Policy，Requirement 为 `PermissionRequirement(code)`。
4. `PermissionAuthorizationHandler` 通过 `IPermissionService.GetPermissionsAsync(userId)` 判断；结果用 `IMemoryCache` 缓存 1–5 分钟，角色修改时清除。
5. **默认拒绝**：设置 `FallbackPolicy = RequireAuthenticatedUser()`；再写一个单元测试，反射扫描所有 Action，要求每个都带 `[HasPermission]` 或 `[AllowAnonymous]`，防止漏配。
6. Application 不再检查权限。后台任务等非 HTTP 调用以“系统身份”运行，在任务入口处说明，不在 Service 内分支。

> 参考：Microsoft《Policy-based authorization in ASP.NET Core》《Custom authorization policy providers》
> https://learn.microsoft.com/aspnet/core/security/authorization/policies
> https://learn.microsoft.com/aspnet/core/security/authorization/iauthorizationpolicyprovider

---

## 5. 输入校验 vs 业务校验

| 类型 | 例子 | 位置 | HTTP |
|---|---|---|---|
| 格式校验 | 必填、长度、数量 > 0、分页大小 ≤ 200 | DTO 特性或 FluentValidation，`[ApiController]` 自动执行 | 400 |
| 业务规则 | 已过账不能再过账、超出采购未收数量 | Domain / Application 抛 `BusinessRuleException` | 422（或 409） |
| 资源不存在 | 单据 ID 不存在 | Application 抛 `NotFoundException` | 404 |
| 并发冲突 | Version 不一致 | Infrastructure 抛 `ConcurrencyException` | 409 |

---

## 6. 单据写入：事务、并发、幂等

### 6.1 三种保护分别防什么
| 机制 | 防什么 | 是否必需 |
|---|---|---|
| **数据库事务** | 库存、流水、单据状态、审计只写一半 | 所有多表写入都必需 |
| **乐观并发 Version** | 两个人同时改同一张单，后者覆盖前者 | 所有可编辑单据必需 |
| **行锁 FOR UPDATE** | 两张单同时扣同一个库存余额导致负库存 | 仅在“读后写共享资源”时使用 |
| **幂等键（命令重放）** | 网络超时后客户端重发，导致同一操作执行两次 | 仅正式单据命令 |
| 前端按钮禁用 | 用户连点 | 前端负责，后端不依赖 |

隔离级别统一使用 PostgreSQL 默认的 **Read Committed**，配合 Version 和必要的 `FOR UPDATE`。不默认使用 Serializable：它需要客户端/服务端处理序列化失败重试，而上述显式机制已经能清楚说明保护了什么。
> 参考：PostgreSQL《Transaction Isolation》《Explicit Locking》
> https://www.postgresql.org/docs/current/transaction-iso.html
> https://www.postgresql.org/docs/current/explicit-locking.html

### 6.2 命令重放（幂等）用大白话解释
场景：用户点“过账”，服务器已经过账成功，但响应在网络上丢了。客户端只看到“超时”，不知道成功没有。
- 如果客户端再发一次普通请求：可能再过账一次（若没有状态保护），或收到“已过账”错误，用户误以为失败。
- 做法：客户端每次操作新生成一个 `CommandId`，放在**请求体**（不用 HTTP Header），超时重试时复用同一个 `CommandId`。服务器第一次执行成功后，把 `CommandId` 和响应结果一起存进现有的 `command_receipt` 表（实体 `CommandReceipt`，沿用，不改名）。
  - 再次收到同一个 `CommandId`：不再执行业务，直接把存下的结果返回 —— 这就叫“重放”（replay）。重放的是**上次的响应**，不是重新执行业务。
  - 同一个 `CommandId` 但请求内容不同（用请求体哈希比较）：返回 422，说明客户端用错了 `CommandId`。
  - 因为结果记录和业务数据在同一事务提交：要么都有，要么都没有，不存在“业务成功但没记录”。

哪些需要：过账、确认、取消确认、作废、审核等**状态流转命令**和库存变动。
哪些不需要：查询；草稿保存（Version 冲突已足够，重复保存无害）；主数据维护。

> 概念参考（本项目只借用幂等键语义，不使用其中的 HTTP Header 传递方式）：IETF《The Idempotency-Key HTTP Header Field》草案；Stripe API Idempotent Requests
> https://datatracker.ietf.org/doc/draft-ietf-httpapi-idempotency-key-header/
> https://docs.stripe.com/api/idempotent_requests

### 6.3 公共组件：`ICommandExecutor`（全仓唯一，决策 #28）
```csharp
public interface ICommandExecutor
{
	// 只用于正式单据命令：事务 + 幂等。普通写入直接用 IUnitOfWork.ExecuteAsync（第 15 节），不经过本组件。
	Task<T> ExecuteIdempotentAsync<T>(Guid commandId, string commandType, Guid? targetId, object request,
		Func<CancellationToken, Task<T>> work, CancellationToken ct);
}
```
内部行为（通过 `IUnitOfWork.ExecuteAsync` 开启事务，自身不直接开启/提交）：
```
开事务(Read Committed)
  在事务内查 command_receipt(command_id)
	存在 → 依次比对 FingerprintVersion、CommandType、ActorUid、RequestTargetUid、RequestFingerprint
	  FingerprintVersion 或 ResultVersion 与当前不同 → 409 回执不可读，不重新执行
	  其余任一不同                                 → 422 command_id_reused
	  全部相同                                     → 返回存档结果（重放）
  result = work()
  插入 command_receipt(...)；唯一索引冲突 → 409 command_in_flight
  提交（由 IUnitOfWork 判定提交阶段，见 7.1）
异常 → 回滚 → 原样抛出（不转换、不记日志）
```
- **指纹兼容**：沿用现有实现的规范化方式、算法与 `FingerprintVersion` 当前值；ReferenceDesign 中的 `SHA256(JSON)` 与 `=1` 仅为示意。验收须有用现有实现生成的回执样本做“同输入同指纹”的金样测试。
- **同 CommandId 并发**（决策 #28e、#31）：服务端不做特殊处理，不加锁、不改表、不回查。后到请求允许先失败并整体回滚：命令内先校验 `ExpectedVersion` 再做状态检查，因此后到者得到版本冲突（409）或回执唯一索引冲突（409 `command_in_flight`），而不是 422。已提交回执的重放行为不变。
- **客户端重试与停止条件**：收到 409 `command_in_flight`/`concurrency_conflict`、503 `outcome_unknown` 或 500 时，用**原 CommandId 和原请求内容（含原 ExpectedVersion）**自动重试一次；得到重放即完成。仍为 `concurrency_conflict` 则是真实版本冲突：停止重试，刷新并让用户重新确认，新操作使用新 CommandId。禁止无限重试，禁止换掉 ExpectedVersion 后复用原 CommandId。

它**不做**：授权、输入校验、异常转 HTTP、写请求日志、普通写入的事务。

### 6.4 指纹/请求哈希只包含用户意图
哈希字段：操作名、目标单据 ID、ExpectedVersion、用户提交的明细。**不要**把数据库实体整行序列化进哈希（无关字段变化会导致误判）。

“预览后再提交”的场景：提交命令带上预览时的 `Version`；提交时 Version 不同即 409，要求重新预览。不需要额外的“依据指纹”，除非预览依赖的数据不在单据 Version 覆盖范围内（此时让那些数据也有 Version，并一并提交）。

---

## 7. 错误处理

### 7.1 唯一出口
- Application/Domain/Infrastructure **只抛异常**，不 catch 后转换。
- Api 注册一个 `IExceptionHandler`（.NET 8+ 原生），映射为 ProblemDetails：

| 异常 | 状态码 | `type`/错误码 | 客户端动作 |
|---|---|---|---|
| 校验失败（自动） | 400 | `validation` | 修改输入 |
| 未认证 | 401 | — | 重新登录 |
| 无权限 | 403 | — | 提示无权限 |
| `NotFoundException` | 404 | `not_found` | 刷新列表 |
| `ConcurrencyException` | 409 | `concurrency_conflict` | 重新加载单据后再操作 |
| `BusinessRuleException` | 422 | 具体业务码，如 `inbound.already_posted` | 显示消息 |
| `CommandIdReusedException` | 422 | `command_id_reused` | 程序错误，生成新 CommandId |
| `CommandInFlightException` | 409 | `command_in_flight` | 用**同一个 CommandId** 重试 |
| 回执版本不可读 | 409 | — | 不重新执行；联系开发者 |
| 提交前的序列化失败/死锁/锁超时 | 503 | `transient` | **确定未生效**，可自动重试 |
| `CommitOutcomeUnknownException`（提交本身失败） | 503 | `outcome_unknown` | 用**同一个 CommandId** 重试 |
| 其他 | 500 | `unexpected` | 是否生效由事务阶段和回执判定：幂等命令用同一 CommandId 重试（已生效则重放回执）；普通写入重新加载数据后再决定 |

“是否生效”的口径（决策 #27）：只有 `IUnitOfWork`/`CommandExecutor` 能判定事务阶段，不能只凭 HTTP 状态码推断。提交前抛出的异常 → 已回滚、确定未生效（含 400/404/409/422 和 `transient` 503）；提交本身失败 → 503 `outcome_unknown`；其余 500 不承诺未生效。

ProblemDetails 中带上 `traceId`，用户报错时提供给开发者对日志。

> 参考：Microsoft《Handle errors in ASP.NET Core APIs》、RFC 9457
> https://learn.microsoft.com/aspnet/core/fundamentals/error-handling-api
> https://www.rfc-editor.org/rfc/rfc9457

### 7.2 返回值约定
- 成功直接返回 DTO（200/201）或 204；不再包一层 `ServiceResult{Success, Data, Error}`。HTTP 状态码本身就是成功/失败标志。
- Refit 客户端通过 `ApiException` 读取 ProblemDetails；在客户端写一个统一的错误解析器即可。

---

## 8. 日志与审计

| 类别 | 目的 | 内容 | 位置 | 存储 |
|---|---|---|---|---|
| 请求日志 | 运维排查 | 方法、路径、状态码、耗时、用户、TraceId | `UseSerilogRequestLogging` | 文件 |
| 异常日志 | 排查 Bug | 完整堆栈 | **只在 `IExceptionHandler` 记一次**（4xx 记 Information，5xx 记 Error） | 文件 |
| 业务日志 | 关键步骤 | 少量 `LogInformation`，如“入库单 {No} 过账，{Count} 行” | Application，按需 | 文件 |
| 业务审计 | 谁在何时对哪张单做了什么 | 用户、操作、单据、前后状态 | Application，**与业务同事务** | 数据库表 |

规则：
- 用结构化日志（`{DocumentNo}` 占位符，不拼接字符串）。
- 关联 ID（决策 #26）：使用 `Activity.Current.TraceId`（无 Activity 时用 `HttpContext.TraceIdentifier`），由 Serilog 记录并在 ProblemDetails `traceId` 返回；不自写 CorrelationId 中间件，客户端不发送自定义关联头。现有审计等表中的 CorrelationId 列结构不改，改为写入该 TraceId。
- 不记录密码、Token。
- 审计不能用日志代替：日志可丢、可轮转，审计是业务数据。

> 参考：Microsoft《Logging in .NET》；Serilog.AspNetCore
> https://learn.microsoft.com/aspnet/core/fundamentals/logging/
> https://github.com/serilog/serilog-aspnetcore

---

## 9. 三种 API 模板

### 9.1 查询
```
[HttpGet] [HasPermission(View)] → XxxQueries.SearchAsync(query) → Dapper SQL → PagedResult<XxxListItemDto>
```
无事务、无幂等、无 try/catch。查询服务可以直接写 SQL 返回 DTO（CQRS 的“读侧”简化做法），不必加载实体。

### 9.2 普通写入（草稿新建/保存）
```
[HttpPost/PUT] [HasPermission(Edit)] → XxxService.SaveAsync(cmd)
	→ uow.ExecuteAsync(加载 → 实体.Update(...) → repo.Update(version 条件) → 审计)   // 不经过 CommandExecutor
```

### 9.3 单据命令（过账/确认/作废）
```csharp
// Api
[HttpPost("{id:guid}/post")]
[HasPermission(Permissions.Inbound.Post)]
public Task<InboundPostedDto> Post(Guid id, PostInboundRequest body, CancellationToken ct)
	=> _service.PostAsync(new PostInboundCommand(id, body.CommandId, body.Version), ct);

// Application
public Task<InboundPostedDto> PostAsync(PostInboundCommand cmd, CancellationToken ct)
	=> _executor.ExecuteIdempotentAsync(cmd.CommandId, Operations.Inbound.Post, cmd.Id, new { cmd.Id, cmd.Version }, async ct =>
	{
		var doc = await _inbounds.GetForUpdateAsync(cmd.Id, ct) ?? throw new NotFoundException(...);
		doc.EnsureVersion(cmd.Version);                       // 409
		await _purchaseOrders.EnsureReceivableAsync(doc, ct);  // 跨单据规则，422
		doc.Post(_currentUser.Name, _clock.GetLocalNow());     // 状态机，422
		await _stock.ApplyAsync(doc.ToStockMovements(), ct);   // 余额 FOR UPDATE + 流水
		await _inbounds.UpdateAsync(doc, ct);                  // version 条件
		await _audit.WriteAsync("Inbound.Post", doc.Id, ct);
		return doc.ToPostedDto();
	}, ct);
```
读这段代码即可看到全部业务步骤；事务、幂等、异常转换都不在视野内重复出现。

---

## 10. 横向：文件夹与命名

### 10.1 文件夹（按功能，不按技术类型堆叠）
```
src/
  SwiftExport.Api/
	Program.cs
	Infrastructure/              # 仅 Api 层的横切：Auth、ExceptionHandling、Logging、OpenApi
	  Authorization/HasPermissionAttribute.cs, PermissionPolicyProvider.cs, PermissionHandler.cs
	  Errors/GlobalExceptionHandler.cs
	Features/
	  Inventory/Inbounds/InboundsController.cs
	  Procurement/PurchaseOrders/PurchaseOrdersController.cs
  SwiftExport.Application/
	Common/                      # ICommandExecutor、异常类型、ICurrentUser、IAuditWriter
	Inventory/Inbounds/
	  InboundService.cs          # 写用例
	  InboundQueries.cs          # 读用例
	  Commands/PostInboundCommand.cs
	  IInboundRepository.cs      # （若仓储接口放 Application）
	Procurement/PurchaseOrders/...
  SwiftExport.Domain/
	Common/Entity.cs, DomainException.cs
	Inventory/Inbounds/InboundDocument.cs, InboundLine.cs, InboundStatus.cs
  SwiftExport.Infrastructure/
	CommandExecutor.cs, CommandReceiptStore.cs
	Inventory/Inbounds/InboundRepository.cs, InboundQueries.Sql.cs
  SwiftExport.Contracts/
	Inventory/Inbounds/PostInboundRequest.cs, InboundDto.cs, InboundListItemDto.cs, IInboundsApi.cs
	Permissions.cs, ErrorCodes.cs
tests/
  SwiftExport.Domain.Tests/  SwiftExport.Application.Tests/  SwiftExport.Api.IntegrationTests/
```
一个功能（如“入库单”）在每个项目中都位于同名路径 `Inventory/Inbounds/`，找代码只需记一个路径。

### 10.2 命名
| 类型 | 规则 | 例 |
|---|---|---|
| Controller | 复数资源名 + `Controller` | `InboundsController` |
| 路由 | 小写、复数、kebab-case；动作用子资源动词 | `GET api/inbounds`、`POST api/inbounds/{id}/post`、`POST api/purchase-orders/{id}/confirm` |
| 写服务 | 单数资源 + `Service` | `InboundService` |
| 查询服务 | 单数资源 + `Queries` | `InboundQueries` |
| 方法 | 动词 + `Async` | `PostAsync`、`ConfirmAsync`、`SearchAsync` |
| 命令（Application 输入） | 动词 + 资源 + `Command` | `PostInboundCommand` |
| 请求 DTO（Contracts） | 动词 + 资源 + `Request` | `PostInboundRequest` |
| 响应 DTO | 资源 + `Dto` / `ListItemDto` / 结果名 | `InboundDto`、`InboundListItemDto` |
| 实体 | 业务名词，无后缀 | `InboundDocument`、`PurchaseOrder` |
| 枚举 | 名词 + `Status`/`Type`，成员英文 | `InboundStatus.Draft` |
| 仓储 | `I` + 实体 + `Repository` | `IInboundRepository` |
| 异常 | 语义 + `Exception` | `BusinessRuleException` |
| 权限码 | `模块.功能.操作` | `Inventory.Inbound.Post` |
| 错误码 | 小写点分 | `inbound.already_posted` |
`inbound_document`、`command_receipt`

禁止的命名：`XxxSupport`、`XxxHelper`、`XxxManager`、`XxxBuilder`（除非确实是构建器模式）等不表达职责的名字；文件名使用英文。

---

## 11. 新增一个单据 API 的清单

1. Contracts：Request/Dto、权限码、Refit 方法。
2. Domain：若有新状态流转，在实体加方法并写单元测试。
3. Application：在 `XxxService` 加方法；正式命令用 `ICommandExecutor.ExecuteIdempotentAsync`，普通写入用 `IUnitOfWork.ExecuteAsync`。
4. Infrastructure：仓储方法与 SQL；UPDATE 带 `version` 条件。
5. Api：Controller Action + `[HasPermission]`。
6. 测试：领域单元测试 + 一个集成测试（未授权 403、成功、版本冲突、同 CommandId 重放）。

**开发者不需要写**：权限检查代码、try/catch、事务开启提交、幂等表读写、请求日志、错误转 HTTP。

---

## 12. 不做的事情（及理由）

| 不做 | 理由 |
|---|---|
| MediatR 及其替代品 | 见第 17 节 |
| 自写 CommandPipeline 重复授权/日志 | ASP.NET Core 中间件与过滤器已提供 |
| 每个模块一个 Support 包装 | 重复且细节不一致，异常处理应唯一 |
| 事务外 advisory 锁、多重命令锁 | 由行锁 + 唯一约束 + Version 替代；删除前逐个调用点证明替代手段（决策 #29） |
| 默认 Serializable | 无明确不变量需求时引入重试复杂度 |
| HTTP 冷却/防连点过滤器 | 前端禁用按钮 + 幂等已覆盖 |
| 资源级权限 | 业务不需要 |
| 事件溯源、消息队列、微服务 | 4–5 用户无此需求 |
| `ServiceResult` 包装 | 与 HTTP 状态码重复 |

---

## 13. 验证矩阵（以“入库过账”为例）

| 场景 | 期望 |
|---|---|
| 未登录 / 无权限 | 401 / 403，数据库无任何写入 |
| 正常过账 | 200；库存余额、流水、单据状态、审计、幂等记录同时存在 |
| 同 CommandId 重发 | 200，返回相同结果，流水不增加 |
| 同 CommandId 不同内容 | 422 |
| 同 CommandId 并发 | 一个 200；另一个 409（`command_in_flight` 或版本冲突，不是 422）且无写入，用原 CommandId 重试得到重放 |
| 版本真实过期后用原 CommandId 重试 | 仍 409 `concurrency_conflict`；客户端停止重试，刷新后用新 CommandId |
| 回执 FingerprintVersion/ResultVersion 不同 | 409，不重新执行 |
| 两个不同 CommandId 同时过账同一张单 | 一个 200，另一个 409 或 422（已过账） |
| Version 过期 | 409 |
| 写流水后故意抛错 | 500，所有表均无写入，日志仅一条堆栈 |
| 两张单同时入同一仓同一品 | 余额 = 两者之和 |
| 提交前取消请求 | 无写入 |
| 模拟提交异常 | 503 `outcome_unknown`；同 CommandId 重试得到重放或正常执行 |

---

## 14. 给 AI Agent 的约束（建议替换现有冗长规则）

1. 遵守本文第 3 节分层“做/不做”。
2. 不新增横切包装类；需要横切能力时修改 `GlobalExceptionHandler`、`CommandExecutor` 或授权组件。
3. 新增端点必须带 `[HasPermission]`，否则测试失败。
4. 不在业务代码中 catch 后返回错误对象。
5. 不引入新库，除非在 PR 描述中说明本文哪一条无法满足。
6. 注释说明“为什么”，不复述代码。

规则越短，Agent 越不容易为满足规则而生成多余层次。

---

## 15. DbSession 与 UnitOfWork：要不要建

### 15.1 “一个请求就是一个工作单元”这句话对不对
这句话来自 EF Core：`DbContext` 本身就是 UnitOfWork，它在内存里跟踪所有修改，**调用 `SaveChanges()` 的那一刻**才一次性写库并提交。所以 EF Core 项目“不用再建 UnitOfWork”是对的。

本项目用 **Dapper**：没有变更跟踪，每条 `Execute` 立即发往数据库。如果不显式开事务，每条 SQL 都是独立自动提交的。因此对 Dapper 而言，“请求 = 工作单元”**不会自动成立**，必须有人明确说“从这里开始、到这里提交”。

结论：**需要一个事务开关，但不需要传统意义上的“大 UnitOfWork”**（持有所有仓储、`uow.Inbounds.Add(...)` 那种）。

### 15.2 现状的问题（源码事实）
- `IDbSession.Transaction { get; set; }` 是公开可写属性（`SwiftExport.Core/Interfaces/Data/IDbSession.cs`）。任何服务都能 `Connection.BeginTransaction()` 然后赋值，于是出现了执行器、ReadSupport、库存服务各自开启或回滚事务的情况。
- `DbSession` 里还挂着 `CommandLease`、`SourceDemandLease`、`ConnectionEpoch` 等 advisory 锁租约状态，连接管理和业务锁混在一起。
- 构造函数中立即打开连接：每个请求即使不访问数据库也会占用一个连接。4–5 个用户下影响很小，但没有必要。

### 15.3 推荐做法
保留 `DbSession` 这个名字和“scoped，一请求一连接”的语义，只收紧接口：

```csharp
// Core 或 Application：仓储只需要读连接和当前事务
public interface IDbSession
{
    IDbConnection Connection { get; }      // 首次访问时才打开
    IDbTransaction? Transaction { get; }   // 只读！
}

// Application：唯一能开启/提交事务的入口
public interface IUnitOfWork
{
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct);
}
```

Infrastructure 中由 `DbSession` 同时实现两个接口（一个类即可）：
```
ExecuteAsync:
  若已有事务 → 直接执行 work（允许嵌套调用，由最外层负责提交）
  否则 BeginTransaction(ReadCommitted) → work() → Commit
  异常 → Rollback → 原样抛出
```

- 仓储通过 `IDbSession` 拿 `Connection` 和 `Transaction` 传给 Dapper。
- 业务代码只能通过 `IUnitOfWork.ExecuteAsync` 拿到事务。
- 第 6.3 节的 `ICommandExecutor.ExecuteIdempotentAsync` 内部调用 `IUnitOfWork`，只多做幂等记录的读写。
- 删除 `DbSession` 上的租约、Epoch 等字段（配合第 6 节删除 advisory 锁）。

这比“不建 UnitOfWork”只多一个接口、一个方法，使事务所有权在 API 层面唯一。但它**不是编译期保证**：`Connection.BeginTransaction()` 仍可被调用。因此由架构测试兜底（决策 #30、#31）：除 `DbSession`（`IUnitOfWork` 实现）外，生产代码中禁止调用数据库事务 API（`DbConnection.BeginTransaction[Async]`、`DbTransaction.Commit[Async]`/`Rollback[Async]`）；按类型判定而非文本扫描，测试夹具和同名业务方法不计入。

> 参考：Martin Fowler《Unit of Work》 https://martinfowler.com/eaaCatalog/unitOfWork.html
> Microsoft《Implement the infrastructure persistence layer with Entity Framework Core》（说明 DbContext 即 UoW）
> https://learn.microsoft.com/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/infrastructure-persistence-layer-implementation-entity-framework-core

---

## 16. Contracts（netstandard2.0）约束

保留并且必须保留：WPF、VSTO（.NET Framework）、后端共用同一份 DTO，避免手抄类型导致字段不一致。Web 前端通过 OpenAPI 生成 TS 类型，不直接引用。

约束：
1. 只放：请求/响应 DTO、枚举、权限码常量、错误码常量、Refit 接口。
2. 不引用 ASP.NET Core、Dapper、Application、Domain。
3. 避免 netstandard2.0 下不可用或需要 polyfill 的类型/语法：`DateOnly`/`TimeOnly`（用 `DateTime`）、`init`/`required`/`record`（需额外 polyfill，统一不用，DTO 用普通 `{ get; set; }` 类）、`Half`、`IAsyncEnumerable`（需包）。
4. 序列化特性只用 `System.Text.Json` 包在 netstandard2.0 可用的部分；不要依赖后端特有的 Converter。
5. DTO 只增不改：新增字段可空或有默认值；删除或改名字段视为破坏性变更，需同时发布三个客户端。
6. 不在 DTO 中写业务方法或校验逻辑（格式校验特性如 `[Required]`、`[StringLength]` 可以保留，`System.ComponentModel.Annotations` 支持 netstandard2.0）。

---

## 17. MediatR：不用，也不找替代，也不自建“中介者”

### 17.1 许可证事实
MediatR 作者在 2025 年宣布转为商业许可（Lucky Penny Software），12.x 为最后的 Apache-2.0 版本；新版对小型组织有免费层，但条款以官网为准：https://mediatr.io/ 。即使免费，也不改变下面的结论。

### 17.2 中介者在本项目能带来什么
| 中介者的卖点 | 本项目是否需要 | 替代 |
|---|---|---|
| Controller 不直接依赖 Service | 否，直接注入 `InboundService` 更易导航 | 构造函数注入 |
| Pipeline Behavior（授权、校验、日志、事务） | 授权/校验/日志已由 ASP.NET Core 完成；事务只需 `IUnitOfWork` | 第 2、6 节 |
| 每个用例一个 Handler 类 | 一个 Service 多个方法更紧凑，相关用例放一起 | Feature 文件夹 |
| 进程内通知（Notification） | 无多订阅者场景 | 直接方法调用 |

代价：每个用例多 `XxxCommand` + `XxxHandler` 两个类型；F12 从 Controller 跳不到实现；调试多几层框架栈。

### 17.3 可选替代品（仅供了解，不推荐采用）
- **Mediator**（martinothamar/Mediator，MIT，源生成器，性能好）：API 与 MediatR 类似，但上面的“不需要”依旧成立。
- **Wolverine**（MIT）：带消息总线、持久化收件箱，远超本项目需求。
- 自己写一个 `IMediator`：等于重造 MediatR，还要自己维护，最不划算。

### 17.4 “自建”的正确范围
需要自建的只有两个小组件，各几十行：
1. `IUnitOfWork`（第 15 节）——事务边界。
2. `ICommandExecutor.ExecuteIdempotentAsync`（第 6.3 节）——在事务内加幂等记录。

其余横切能力用 ASP.NET Core 自带机制：授权 Policy、`[ApiController]` 模型校验、`IExceptionHandler`、Serilog 请求日志。若将来确实出现“同一横切逻辑需要套在几十个用例上且 ASP.NET 过滤器无法表达”的情况，再评估 Mediator（MIT）。
