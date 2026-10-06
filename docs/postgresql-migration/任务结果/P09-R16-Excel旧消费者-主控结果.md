# P09-R16 Excel原生命周期迁PG与旧仓储退出

本批只迁移原 `ExcelReadProfileLocalDbTests.cs` 的一个生命周期方法，改用现有 `PostgreSqlExcelReadProfileRepository` 和隔离PG库。旧 `ExcelReadProfileRepository.cs` 在固定SHA、跟踪状态、HEAD无修改及当前消费者零复核后退出。没有修改业务服务、PG仓储、接口、API、DI、配置或数据库结构；没有拆分生产仓储或新增测试矩阵。

原断言保留：创建与审计、名称规范化、同组重名拒绝/跨组同名允许、分步保存和地址规范化、NULL参考工作表、旧主版本拒绝、字段内部身份与软删、字段写后整批回滚、同版本双连接唯一成功、已有事务归属、SQL等待取消透传与主版本回滚、整份软删后重建。分组改为最多40字符的随机ASCII标识，避免原库已有配置影响空列表断言。

原SQL Server触发器的失败边界通过透明测试代理实现：全部操作调用真实PG仓储，FAIL字段实际保存后，在同一事务执行真实服务器除零错误。原WAITFOR取消通过第二连接只锁本批新字段实现，观察到 `pg_blocking_pids` 的真实4292→7516等待后取消实际UPDATE；失败和取消均按原断言验证头/字段没有部分生效。没有模拟返回值、替换业务规则、创建触发器或其它DDL。

## 实际验证与失败基线

- 必要编译命令：`dotnet build SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore -v minimal`。首次2错误/7既有警告4.95秒，缺测试参数命名空间；补齐后0错误/8警告5.38秒，本批新增可空性警告随后消除。实库前0错误/7既有警告3.54秒；旧源退出后最终0错误/7既有警告4.11秒。
- 实库命令：[原生命周期实际命令](P09-R16-Excel原生命周期-实际命令.ps1)，只选择 `ExcelReadProfilePostgreSqlTests` 原一个方法，其它数据库门禁关闭，环境变量结束恢复。1通过/0失败/0跳过，方法423.5846ms，含原50表保护的测试宿主约2秒。TRX：[本轮结果](../../../TestResults/postgresql-migration/p09-r16-excel-lifecycle-pg.trx)。退出旧源后测试/PG源SHA未变，不重跑已绿实库。
- 仅 `192.168.3.12:5432/dv_next_pg_failure_tests_20261004` 新增3配置头、2字段，共5行保留；原50表52,965行数量及完整行MD5全同，当前52,970行。库存序列95→95。没有修改原记录、运行库或备份库，没有清理、重置、覆盖或同步DDL。逐表保护及新增UID：[数据库证据](../../../TestResults/postgresql-migration/p09-r16-excel-data-protection.json)。结束其它客户端0、advisory锁0、testhost/vstest进程0。
- [本轮文件和命令证据](P09-R16-Excel旧消费者-文件证据.json)、[原2390源码保护](P09-R16-源码保护证据.json)、[收口实际命令](P09-R16-Excel源退出-收口实际命令.ps1)。累计66类型72文件符合预定退出，43测试文件及两个此前工具项目差异已登记，无额外生产源变化、缺失或新源码。5默认忽略的原生成文件仍存在且SHA未变。
- 首次本地PowerShell缩进整理运算符组合错误/源码写权限拒绝没有修改源码或连接数据库，随后用apply_patch及授权PS7处理，不改ACL；一次展示脚本把After当文字误显示50变化，实际属性比较重新核对为0，原测试和保存摘要始终相同。全部实际问题均记录，不计为业务回归。

## 剩余和未验证

当前累计66/76旧候选类型72文件退出，10候选R6/S1/F3待：BomExecution、CommandReceipt、ExecutionEligibility、PackingList、PurchaseOrderDraft、SourceDeliveryQuery；SqlServerSchemaGenerator；DapperRepository、SqlConnectionFactory、SqlServerExceptionClassifier。旧Excel退出后旧通用Dapper仅定义与审计注释引用，留给F公共阶段串行接管；混合工厂仍须保留共享接口。箱单LocalDB行为/旧交付依赖和源结构测试先迁有效断言，再退出对应旧源。

最终源包/资产图/生成结构契约/宿主及阶段回归未运行，不将本轮1实库或历史通过提升为整体完成。Q01、已记录范围外测试失败和B03本位币切换缺口保留。执行资格旧源自动审批此前因含四条未提交准备语句拒绝删除，原文件SHA不变，仍等待此前针对性授权；本轮没有重试或绕过。

主控本批STOPPED，整体目标active，A/B没有续派。未提交、合并、推送或部署，所有其它未提交成果保留。
