# P08 正常HTTP登录及基础查询：限定验收

2026-10-05 17:50，主控串行。只在既有 `SwiftExport.Application.Tests/Database/PostgreSqlRuntimeWiringTests.cs` 增加一个真实PG/真实HTTP定向场景；未修改生产源码、DI生命周期、认证/权限规则、API契约、用户配置或仓储组织，没有新框架或机械拆分。

## 实际正常链

使用真实WebApi程序集入口及 `AddWebApiServices`、`UseWebApiPipeline`，发现 **271个Action**，原AuthController.Login存在。门禁只接受指定独立库和已登记批次；本地随机HTTP端口11818，finally关闭宿主。未替换CurrentUser、AuthRepository、PermissionEvaluation、TokenFactory或凭据版本验证器，未伪造JWT。DevUser在本测试内存明确关闭，不改用户配置；正常用户名/密码登录签发令牌，后续请求使用该原令牌。

|实际请求|状态与断言|
|---|---|
|GET `/api/auth/me` 匿名|401|
|POST `/api/auth/login` 错误密码|401|
|POST `/api/auth/login` 正确测试输入|200，取得原Access_token响应，不输出Token|
|GET `/api/auth/me` 原登录Token|200，实际测试UserId、SYSTEM_ADMIN角色、IsAdmin=true、112个权限|
|GET `/api/sysdiccurrency/enabled`|200、Success=true、数组2项|
|GET `/api/productcategory/all`|200、Success=true、数组34项|
|POST `/api/product/page` PageIndex1/PageSize2|200、Success=true、实际PageIndex1/PageSize2、Items2、TotalCount17312|
|GET `/api/inventory/warehouse-sites/picker?PageIndex=1&PageSize=2`|200、Success=true|

这是 **一个真实场景、8个HTTP请求**，不是8个独立测试或完整API验收。未测试普通非Admin授权、账号注册/改密码、HTTP写入单据、凭据失效或所有模块路径，不以基础查询覆盖这些未运行项。

## 实际命令及失败分类

```powershell
dotnet build SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore -p:UseSharedCompilation=false -v quiet
dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~RealPg_RuntimeAuthenticatedHttpQueries' --results-directory TestResults/postgresql-migration --logger 'trx;LogFileName=p08-authenticated-http-webapi-entry.trx' -v minimal
```

- 每次新增夹具修正后只编译受影响项目，最新 **0错误/10既有NU1900及MSTEST警告**；没有普通单元或全量重复。
- `p08-authenticated-http-first.trx`：**1失败**，新测试从root解析scoped IPasswordHasher被验证拒绝，尚未建立专用输入；仅改测试为CreateScope，不把Hasher生命周期改singleton。
- 同过滤，`p08-authenticated-http-scoped-fixture.trx`：**1失败**，两专用输入已提交，正确登录仍401；原输入保留，不重置密码或改认证规则。
- 同过滤，`p08-authenticated-http-discovery-diagnostic.trx`：**1失败**，明确输出ApplicationName=testhost、ActionCount=0、discoveredLogin=false。保留账号的BCrypt密码核对通过；401来自空Controller测试宿主的fallback授权，未进入真实AuthController，不分类为PG认证故障。
- 仅在新HTTP测试WebApplicationOptions指定真实WebApi ApplicationName，增加原Login发现断言；`p08-authenticated-http-webapi-entry.trx`：**1通过/0失败/0跳过**，实际8请求全部执行。两原输入复用但此前未执行的真实登录/JWT/权限/查询分支本次首次全部成功，不能累计三失败为通过。

测试文件SHA256：`19683574F3C46821706E794552CA0FA85B544B1348482C8A9883DA99AEB4F21D`。没有为绿色统计重新建立其他批次或重复运行通过场景。

进程设置 `SWIFTEXPORT_RUN_PG_FAILURE_TESTS=1`、`SWIFTEXPORT_PG_FAILURE_TEST_CONNECTION`、`SWIFTEXPORT_PG_RUNTIME_HTTP_BATCH=516cf0ea-1d68-4b14-b047-c1df88852008`，从用户配置安全读取凭据后内存限定failure库；其他测试门禁关闭，finally恢复环境。密码/哈希/Token/连接字符串均未输出到工具或结果。原应用HTTPS中间件保留；本地随机HTTP宿主没有HTTPS端口的警告属于此验收环境，不修改用户HTTPS配置。

## 数据与成果保护

唯一写入 `192.168.3.12:5432/dv_next_pg_failure_tests_20261004/public`，50表 **17785→17787**。两专用输入：SysUser `516cf0ea-1d68-4b14-b047-c1df88852008`、SysUserRole `516cf0ea-1d68-4b14-b047-c1df88852009`；仅关联现有有效SYSTEM_ADMIN角色 `51de552e-d96c-4aad-9478-59e07084c43f`。测试事务一次建立两条，重跑只验证原账号/关系/密码/凭据版本，不更新、删除或覆盖。

这是测试身份夹具建立，不宣称生产Admin注册或授权HTTP流程已验。没有修改原账号、密码、角色、权限、业务余额、号段、流水、回执、审计或结构，不写dv_next或备份，不清库/重建/禁触发器。无需同步DDL。

验收后精确排除两新UID，剩余 **50表/17785行全部MD5与运行前一致**；仅两个新输入可见。库存序列和号段不因HTTP只读查询消费，本轮没有调用取号或库存命令。

[两输入清单](P08-正常HTTP登录-新增输入清单.csv)、[运行前摘要](P08-正常HTTP登录-运行前摘要.csv)、[最终摘要](P08-正常HTTP登录-最终数据摘要.csv)、[精确排除原数据核对](P08-正常HTTP登录-原数据核对摘要.csv)。

## 状态

本轮工具及宿主结束、端口释放，A/B仍已停止，无新派工。成果尚未提交/推送/部署，整体目标active。本轮补齐独立库正常HTTP认证与基础查询，**dv_next完整启动及登录HTTP仍未验证**，此前共享库启动审批拒绝没有绕过。P08全部验收、B22跨业务/空范围、采购来源关联退出、B07/B10/B19比较决策及P09仍开放。
