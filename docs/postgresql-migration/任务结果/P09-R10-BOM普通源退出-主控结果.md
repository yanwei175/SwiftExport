# P09-R10 BOM 普通仓储旧源码退出

2026-10-06，主控串行。仅删除旧 BomRepository.cs，调整三份现有测试，不修改 PG 实现、业务服务、接口、DI、配置、结构或数据库。[原 SHA、HEAD、测试前后及执行证据](P09-R10-BOM普通源退出-文件证据.json)。旧仓储当前消费者只有这三份测试；转换后仅余旧定义文件，原生用户环境限定路径/SHA/HEAD/消费者复核后删除，退出后当前引用零。

- Database/BomCollectionQueryTests.cs：保留两个现有 PG 场景，原始文本/单次枚举、软删历史与存储编号精确分组、父事务、无额外 JOIN/锁、text 数组全部断言保持。
- Database/BomReferenceQueryTests.cs：保留两个现有 PG 场景，空身份/去重、完整映射及可空组件/旧快照、共享编号资格仅正标记、无父身份或标记数值相等条件、父事务、uuid 数组全部断言保持。
- Database/BomSharedQueryTests.cs：保留 withUpdLock 两个原 PG 场景和 DTO/实体/分页三个原 PG 场景，独立组成/产品/空组、重复及前导空格输入序号、存在判定、父事务和无额外锁断言保持。九个旧 SQL Server 提供者分支随旧实现退役，没有新增用例、移除有效 PG 断言或把 SQL Server 范围锁标为 PG 行锁等价。

另行核对 BomExecutionRepository：无同名 PG 实现；IBomExecutionRepository、IBomExecutionReader 和 BomExecutionReader 当前只由其定义及专用测试引用，未注册 DI，没有运行消费者。本批保留这些文件及全部原端口测试，不为无实际用例的旧端口新增 PG 实现，也不机械迁移其三项 UPDLOCK/HOLDLOCK 静态断言。该候选仍待单独源退出处理，不标完成，不改当前实际共享 BOM/需求读取协议。

## 实际验证

- `dotnet build SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore -v minimal`：0 错误、7 个既有警告，3.90 秒，exit0。
- PowerShell 7：`& './docs/postgresql-migration/任务结果/P09-R10-BOM普通契约-实际命令.ps1'`。[完整命令](P09-R10-BOM普通契约-实际命令.ps1)关闭并恢复14个数据库门禁，排除37个 Integration 分类及其他数据库类别，只选择上述九个既有 PG 场景。9 通过、0 失败、0 跳过，121ms，exit0。[TRX](../../../TestResults/postgresql-migration/p09-r10-bom-query-contracts-offline.trx)，SHA `22E52F61A4395F369C84858DA794E0FA9AFA547EF4265D171C0FBC0A218F5A56`。
- 实际 diff 检查通过。[累计保护](P09-R10-源码保护证据.json)：固定2390份快照中58个登记旧文件缺失、33个测试和此前两项目变化，无额外生产变化/缺失/新源码；五个默认忽略的原生成产物仍逐 SHA 核对未变。

本批离线结果不代替真实 PG SQL执行、约束及并发验收，未重复已绿实库或全量测试。累计52/76候选类型、58旧文件退出，剩24候选 R20/S1/F3；包、有效旧行为测试及最终资产图/宿主仍待。Q01及其他范围外失败、B03保留。A/B未续派，未提交、合并、推送或部署，目标 active。
