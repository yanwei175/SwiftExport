# M23 源 Dapper 排除列表只读证据

2026-10-05。主控针对出库 SearchItems 审查中唯一待确认的 nullable `List<Guid>` 展开结果，使用实际 SqlClient/Dapper 2.1.66、固定 SQL Server `192.168.3.12 / dv_next`；读取 WebApi 现有配置仅在进程内设置源只读环境变量，不输出凭据。

新增 `SwiftExport.Application.Tests/Database/SqlServerOutboundExclusionProtocolTests.Integration.cs`。与请求同类型的 `List<Guid>? ExcludedProductIds` 直接作为匿名参数传入原 `NOT IN @ExcludedProductIds`。查询候选仅两项随机 Guid 的 VALUES/CAST 常量，不读取或写入业务表、不建临时对象、不开始事务；每项另外确认 `@@TRANCOUNT=0` 及连接 SELECT 1 可用。

当前测试 SHA256 `67E82DCE0683B5FCBF5F279AFB2E292EA51AD8B1EB391940646011545F5FFCCF`。

| 输入 | 返回数 | 当前结果 |
|---|---:|---|
| 空 List | 2 | 通过 |
| null List | 2 | 通过 |
| 排除第一个 ID | 1 | 通过 |
| 重复排除第一个 ID | 1 | 通过 |
| Guid.Empty | 2 | 通过 |

实际命令：设置源显式门禁与连接、清四个 PG 启用开关后运行：

```powershell
dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore -p:UseSharedCompilation=false --filter FullyQualifiedName~SqlServerOutboundExclusionProtocolIntegrationTests --results-directory TestResults/postgresql-migration --logger 'trx;LogFileName=m23-source-exclusion-actual-readonly.trx' -v minimal
```

5 通过、0 失败、0 跳过，约10秒；编译0错误，输出3条已有 NU1900 网络警告。首轮 `m23-source-exclusion-first-readonly.trx` 因未配置源连接门禁5项跳过，保留且不计通过。此次为真正源数据库执行结果，不是手写 NOT IN(NULL) 或模拟 ADO 的替代证据；没有截取实际发出 SQL 文本，因此不承诺具体展开文本。

据此冻结本方法局部 null→空 UUID 数组，与非空 `<> ALL(uuid[])` 配合；保留重复/Guid.Empty，服务原 null/501 拒绝不改。不能据此宣称全选品 SQL、非法分页内部异常时序、SKU/Unicode 比较或真实 PG 物化已通过。执行 A 按独立新分派实施，主控再串行验收。
