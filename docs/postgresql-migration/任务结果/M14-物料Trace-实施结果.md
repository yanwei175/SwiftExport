# M14-S03-T01-IMPL-01 物料 Trace 实施结果

执行 A 已完成本轮授权实现和独立离线验证，结果先落盘，再将本人状态改为 STOPPED。开始于 2026-10-05 03:52:03 +08:00。源码／测试可供主控精确接回；没有注册 DI、切连接工厂或运行真实数据库。

## 授权与实现

主 Agent 为 Backend，沿用后端 API 工作流及 Dapper、会话、质量门禁技能。按主仓库 `需求Trace实施分派.md` A 段和 `Trace读取协议冻结.md` 实施；源 Trace、Core 接口、三个 DTO、Application 调用方均与本 worktree 忽略换行后相同。读取时主仓库 HEAD 为 `40d166322dc962e1f5a8695af168d883475e7dba`，本 worktree 仍为 detached `56209d97a00533c938dd527fa9d37683064a11e8`。

本轮实际业务源码变更为新增 PostgreSQL Material Trace；主文件仅新增接口 using、声明完整 `IMaterialDemandQueryRepository` 及同步中文类型职责说明。主文件从首个既有私有表字段至文件末尾，与主仓库已接回工作台实现逐字相同；两个已验方法及原私有实现没有改动。

Trace 按独立参数化命令串行执行：

1. 当前物料全部 PO Link 的起始采购行 scope，不排历史／删除 Link。
2. 同一会话上的共享 ScopedGuard，限定相关入库头并检查整头；空 scope 不扩大到全库。
3. header；随后完整缓冲同 Gross 的未删修订 tree，再判断空头返回 null。
4. 一次 SQL eligible/ranked／收货／单位检查，缓冲 LinkId、TargetLineId、decimal? 覆盖结果。候选范围重新读取该阶段的当前 Link，不使用起始 scope 替代。
5. 以等长 uuid[]／uuid[]／numeric[] 和显式 cast、多数组 unnest 绑定冻结覆盖，采购展示独立重新读取收货与最新反馈。
6. 逐字保留源 C# 汇总与 GetSourceReviewReason。associated 超量仍保留原数值，reason 提示超量、coverage 未知；Ordinal 来源／指纹比较及 IsNullOrWhiteSpace 不改。

没有仓储事务、连接创建、临时表、函数、锁、额外 Guard、生产测试回调或新公共框架。所有阶段借用当前会话及可空外层事务，并传原 token；预取消在第一阶段前停止。调用者事务的提交、回滚、隔离及 aborted 恢复仍由调用者负责。

覆盖窗口保留日期 NULL 最后、SourceLineNumber NULL 最后、LinkId 的源 SQL Server 字节优先级 `10,11,12,13,14,15,8,9,7,6,5,4,3,2,1,0` 及 ROWS；没有增加拆解行号。最新反馈也保留 Sequence DESC＋同字节优先级 Uid DESC。PG quantity、boolean、显式 CLR DTO 别名和 ASCII 空格／字典 SQL 比较按既有 PG 约定适配；没有展开 B07/B10 探针或统一工作台／Trace 的原业务差异。

## 固定交付 SHA256

以下路径均相对于本人 worktree `C:/Users/BigBigBigDv/.codex/worktrees/9b64/SwiftExport`；主控只接回这四份已分派代码文件及本结果，不复制其他未跟踪产物。

| 文件 | SHA256 |
| --- | --- |
| `SwiftExport.Infrastructure/Repositories/DemandPlanning/Materials/Query/PostgreSqlMaterialDemandQueryRepository.cs` | `122997F741600E843A83B17350E8BDA2A6696373B17D7625FFBFB9859A58695F` |
| 同目录 `PostgreSqlMaterialDemandQueryRepository.Trace.cs` | `F8DF0B2B51BDE6278B18C52C7B800016F720B5D4B75C9C4C69D81F8A02EE1742` |
| `SwiftExport.Application.Tests/Database/PostgreSqlMaterialDemandTraceMigrationTests.cs` | `53619DF2D0D638939854920061D4ED414548405D8C2E181C22954C5CCEB0799E` |
| 同目录 `PostgreSqlMaterialDemandTraceMigrationTests.Integration.cs` | `3B477F16EF104EFF49E21B392616CF2944B5AB734F3CA2CFEC961BF8CD2C41A8` |

只读共同依赖已精确同步，逐份与主仓库归一换行比较相同，本轮未编辑：

- `PostgreSqlInboundReceiptHistoryGuard.cs`：`C6578ADD901BBDE13B8178A06A5AB00CE0126C2D938AE534FFF8BE0C703E291C`。
- `UnverifiedReceiptHistoryException.cs`：`574BE8712E8C62DD0E05AB063F517012F82E2A1B80F9C79E0040BF855A5B9234`。
- `PostgreSqlExceptionClassifier.cs`：`8C4F2808D2B6F1267490C142099C69C3ACD7D6C424E8051890602A0DC3E5C84F`。其相对于旧 detached HEAD 的 tracked diff 是主控停写同步内容，不是本人越界修改。

## 已运行命令与证据

必要受影响构建：

```powershell
dotnet build SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore -p:UseSharedCompilation=false -v minimal
```

初次构建成功，0 错误、7 既有 MSTest 警告。后续集成测试代码曾因测试 UUID 字节变量与返回 DTO 同名发生编译错误，已改名修复；最终测试命令再次完成所有受影响项目编译和本轮测试。

最终严格清理四个实际 PG 开关及源只读开关后，仅运行独立离线类：

```powershell
$env:SWIFTEXPORT_RUN_PG_TESTS=$null
$env:SWIFTEXPORT_RUN_PG_FAILURE_TESTS=$null
$env:SWIFTEXPORT_RUN_PG_COMMAND_TESTS=$null
$env:SWIFTEXPORT_RUN_PG_RBAC_TESTS=$null
$env:SWIFTEXPORT_RUN_SQL_SOURCE_READ_TESTS=$null
dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore -p:UseSharedCompilation=false --filter 'FullyQualifiedName~PostgreSqlMaterialDemandTraceMigrationTests' --results-directory TestResults/postgresql-migration --logger 'trx;LogFileName=m14-material-trace-offline.trx' -v minimal
```

最终 **20 通过、0 失败、0 跳过**，TRX 为 `C:/Users/BigBigBigDv/.codex/worktrees/9b64/SwiftExport/TestResults/postgresql-migration/m14-material-trace-offline.trx`。早期精确过滤同一离线类也没有进入数据库测试；最后一次使用上述准确开关名称重跑确认。增量最终输出没有新增警告；完整编译时仍是上述 7 条既有警告。

离线测试通过真实公开仓储／服务调用及 Dapper 的 reader／scalar 通道，验证六阶段顺序、独立参数、冻结空值／空数组、DTO 显式别名、原理由优先级、超量数值、原 Guard 服务消息、空头仍读取 tree、无事务／已有事务及所有阶段 token。其数据表是替身，不证明 PG SQL、覆盖算法或真实驱动取消已经通过。

最终检查：已验工作台实现逐字相同；原 Trace C# 汇总及来源理由逐字相同；授权文件无替换字符／行尾空格；检查 Git diff/status、共享依赖归属和源码指纹。未运行全量测试或旧工作台测试，未修改旧测试、旧结果、接口、DTO、服务、DI、共享 Guard/classifier、结构或主清单。

## 已编写、未执行的真实 PG 测试

独立 Integration 类包含 **46 个场景**，已随测试项目编译；只允许主控串行使用现有严格故障库门禁 `192.168.3.12:5432 / dv_next_pg_failure_tests_20261004`。每例随机实体／UID在一个 ReadCommitted 夹具事务里按 PG 元数据 INSERT，finally 回滚，不调用通用 Add 重置身份，不禁用 CHECK，不提交／清库／初始化 DDL。

场景涵盖：6+6/7→6/1、反向普通 PG UUID 顺序的源尾键、日期与来源行号优先／NULL 最后；覆盖后正式收货变成 8 而仍复用 6/1、外围 1；起始 scope 后新增目标 Link 被后续覆盖读取且只 Guard 一次；版本／指纹／MD/SO版本／客户／单位／零量／历史／缺目标／错客户／错单位／超量；其他坏贡献使整个覆盖分区未知；整采购行报告最新修正量、历史取消 Link、未删旧修订树；相关整头／无关坏历史、历史 Link 和软删 scope 行；无头／软删头／无外层事务；预取消／六阶段命令前取消／各阶段真实服务器独立只读等待中取消、aborted 由调用者回滚后复用连接。

阶段变化由测试真实 Npgsql 命令包装器在上一 reader 结束、下一命令执行前控制；不改生产 SQL，不加生产回调。当前阶段变动使用同一调用者事务内自写，**不冒称跨连接并发验收**。服务器等待取消也是测试专用独立只读命令，不将 pg_sleep 混入生产 Trace 或隐式 batch；真实是否符合预期须主控运行。

## 未验证及需主控处理

1. 本窗口未连接任何数据库，未运行上述 46 场景。真实 SQL 语法、Npgsql nullable numeric[]、业务量、Guard／取消／reader释放、运行性能及实际结构仍需主控验收；不能把 20 离线通过或历史工作台通过数算作新 Trace 实库通过。
2. 当前 PG DDL 有 `ux_supplier_progress_line_sequence` 唯一索引，禁止同一采购行 Sequence 并列。生产保留源 Uid DESC 尾键，离线核对表达式；真实文件改为验证该索引拒绝并列（23505／约束名），未绕约束插入。若主控需要实库并列选择探针，须另行确定合法只读探针范围。
3. 未提交跨连接夹具，未执行无事务跨连接变动及 RepeatableRead 可见性；冻结文档要求此证据由主控安排独立目标／精确清理范围。本轮阶段自写测试不能代替它。
4. 按冻结协议源 provider 的预读深度、畸形数据映射错误与尾部技术错误可见范围不保证跨驱动完全同一。实现保留 header→缓冲 tree→空头 null 的最小稳定边界。
5. 负量／非法 Link 等受 CHECK 禁止的夹具未绕过结构造数；B07/B10 范围保留。没有运行接线、部署、Git 提交或推送。旧未跟踪迁移源码／测试／结果保留，编码文件为 UTF-8；辅助 Python 输出曾因终端代码页显示乱码，交付文件已检查无替换字符。

本人唯一状态入口：`C:/Users/BigBigBigDv/.codex/worktrees/9b64/SwiftExport/docs/postgresql-migration/窗口状态/执行A.md`。主控直接读取，不发送其他聊天消息。
