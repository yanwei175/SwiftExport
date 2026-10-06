# M14-S02-T01-IMPL-01：PostgreSQL Gross Trace 实施结果

## 交付结论

已新增 Gross Trace 仓储两个 partial 和两份独立测试。最终 **36 离线通过、0 失败、0 跳过**；真实 PG 的 **39 个场景仅编写及编译，未连接执行**。本轮实现源码切片可评审，不代表 PG SQL/整体需求迁移或运行接线验收通过。下一步由主控在本人 STOPPED 后按下列固定 SHA 精确接回，并串行真实验收。

主 Agent Backend，后端 API 工作流，复用已读 Dapper/DbSession/仓储基础/文件组织/质量门禁规则；实施按主仓库 `需求Trace实施分派.md` B 段、共同门禁和 `Trace读取协议冻结.md`。主仓库只读，未同步整个 worktree。上一轮审查中 Material 窗口包含拆解行号的描述已由主控冻结纠正，本轮没有实施 Material，也没有改旧审查结果。

## 唯一接回文件与指纹

工作目录：`C:/Users/BigBigBigDv/.codex/worktrees/3557/SwiftExport`。

| 文件（相对该工作目录） | SHA256 |
| --- | --- |
| SwiftExport.Infrastructure/Repositories/DemandPlanning/Sources/Query/PostgreSqlGrossDemandQueryRepository.cs | 6B3FD075DF6F45C4C52BC5AFE6A780089E1FC39A6E36C1A32D04FDB1E3AE4F43 |
| SwiftExport.Infrastructure/Repositories/DemandPlanning/Sources/Query/PostgreSqlGrossDemandQueryRepository.Trace.cs | 15A284E82EDC75644D7A8A573EFF45F0C643C35A81974AF97B2CDEF743B3CD35 |
| SwiftExport.Application.Tests/Database/PostgreSqlGrossDemandTraceMigrationTests.cs | BD63578C9A081D8429527CD6AED8E139258A460D4DF7E11A9EEB2812B0A6C1C0 |
| SwiftExport.Application.Tests/Database/PostgreSqlGrossDemandTraceMigrationTests.Integration.cs | F4501F81E4FB6FA21E0EA3331318A39F1CE76846776F2959A95A13008491423D |

另外仅新增本结果，更新 `docs/postgresql-migration/窗口状态/执行B.md`。既有 SQL Server 仓储、Application 服务、Core 接口、Contracts DTO、A 的 Material 两 partial、旧测试/旧结果及主清单均未编辑。

## 实现与原行为

主文件只声明 sealed partial 主构造及既有 RepositoryBaseContext，不声明 IGrossDemandQueryRepository，不补其他三个方法占位、SQL Server 委托或 DI。Trace 借用会话唯一连接和可空外层事务，不建立/结束事务、不改变隔离、不关闭连接，不新增锁、临时表、数据库函数、测试回调或通用框架。

实际公开调用顺序为：一次历史 PO scope → 共享 scoped Guard → header → **完整缓冲 materials → 空头才返回 null** → purchases → deliveries → 非法装箱计数 → 原 DTO 与 C# 汇总。七个独立参数化命令包含 Guard，每个 SQL 透传原取消令牌。scope 包含历史/已删除物料及历史 Link，不新增删除/状态资格；Guard 按冻结采购行集合校验命中的整头，不把空范围扩大成全库，不先查头短路。

Materials 保留所有修订及软删除行，未知 ActivePurchaseQty 与确定零保持区分。采购保留 LEFT JOIN 缺目标、历史释放、客户/单位/来源版本与指纹理由；货好/实收属于整条采购行，不进行需求分摊。Shared 保留 Gross 原有 other Link 不带 TargetType 限制的差异。最新反馈按 Sequence DESC，并用冻结规范 UUID 字节优先级 `10,11,12,13,14,15,8,9,7,6,5,4,3,2,1,0` 的整数数组 DESC 选择原 Uid 尾键，不能替换为普通 PG UUID 顺序。

Deliveries 展示已过账及反向装箱行，反向 Qty 仍为正，但 CountsTowardDelivery=false，不再减一次。尾部计数独立处理孤儿、软删已过账、软删头、错误状态/客户/来源/数量/单位及日期；展示单位的 ASCII 双边修剪与计数的原始比较保留区别。源理由与交付理由仍独立：来源指纹/版本坏仍可确定 DeliveredQty=4，只有原交付理由阻断净量。原 C# Ordinal 来源类型、指纹字符和 IsNullOrWhiteSpace 判定保留，不借 ICU 改写。

SQL 使用显式 DTO 别名，PurchaseOrderLine 原 QTY 对应 PG 实际 quantity；COUNT 显式 integer，uuid 空值补值显式 cast。bit 改 boolean，APPLY 改 LATERAL，TOP 改 LIMIT，NULL 普通 ASC 采用源 NULLS FIRST。单位只修剪 ASCII 空格，文本等值沿冻结 rtrim/ICU 协议；B07/B10 不开展新探针或样例修复。

## 离线测试与必要编译

最终实际命令（均在上述 worktree）：

```powershell
$env:SWIFTEXPORT_RUN_PG_TESTS=$null
$env:SWIFTEXPORT_RUN_PG_FAILURE_TESTS=$null
$env:SWIFTEXPORT_RUN_PG_COMMAND_TESTS=$null
$env:SWIFTEXPORT_RUN_PG_RBAC_TESTS=$null
$env:SWIFTEXPORT_RUN_SQL_SOURCE_READ_TESTS=$null
$env:SWIFTEXPORT_RUN_SOURCE_READ_TESTS=$null
dotnet test .\SwiftExport.Application.Tests\SwiftExport.Application.Tests.csproj --no-restore -p:UseSharedCompilation=false --filter 'FullyQualifiedName~PostgreSqlGrossDemandTraceMigrationTests.Offline_' --logger 'console;verbosity=minimal'
```

最终退出 0：36 通过，0 失败，0 跳过，135 ms。必要依赖及两份新测试均编译成功，0 编译错误，7 项既有其他测试分析器警告，本轮文件无新警告。没有单独全量构建或全量测试；单测试项目所需依赖正常编译。既有 NuGet 资产复用，未恢复包或修改包依赖。

离线测试通过公开 GetTraceAsync 和真实 Dapper/ADO 路径，确认所有阶段与 nullable 事务、令牌、scope 参数、DTO 默认/NULL/枚举/decimal/日期、空头第二集合失败可见、原来源与交付理由、每阶段技术失败/等待取消、原服务 Guard 消息、reader 已关闭以及事务/连接所有权。预取消不是空成功。测试连接的类型名使 Dapper 使用 Npgsql 数组能力，命令由脚本执行且不解释 SQL，**不作为真实 PG SQL 成功证据**。

中间验证记录保留：第一次编译缺少测试的 IDatabaseExceptionClassifier 命名空间，修复测试 using；随后 34 通过、2 失败，因为脚本连接没有模拟 Dapper 的 Npgsql 数组能力，Guard 参数被展开。仅修正独占测试连接的提供者能力标识后 36 通过，未修改共享 Guard、删断言或放宽预期。新 Integration 的一项断言分析器警告已在本轮修正，最终无新警告。

新增未跟踪文件以 `git diff --no-index --check -- NUL <文件>` 检查，没有 whitespace error；退出 1 来自新文件与 NUL 有差异，输出只有 Git 的 LF→CRLF 提示，不是编码损坏。源码已逐段对照源 SQL/C#及实际 diff 内容，未覆盖既有未提交改动。

## 真实 PG 场景代码与未运行范围

39 场景入口均为 `PostgreSqlGrossDemandTraceMigrationTests.RealPg_...`，带 `PostgreSqlGrossDemandTraceIntegration` 分类及 DoNotParallelize，严格使用现有固定故障库门禁 `192.168.3.12:5432/dv_next_pg_failure_tests_20261004`，不提供默认连接/凭据。随机实体按既有元数据 INSERT，保留指定 UID 和 IsDeleted；审计仅打戳。每个有写入场景在一条 ReadCommitted 事务内 finally 回滚，不初始化或清理库，不经通用 Add 改变历史身份，不绕过 CHECK/唯一索引/不可变触发器。

代码覆盖：

- 当前、历史及软删物料；整采购行收货7、反馈量9/nullable 最新反馈、共享关系及同事务自写回读/返回后查询。
- 缺目标、软删采购行、作废目标、客户/单位错、需求版本/指纹/上游版本未知、历史完整释放的数量与理由。
- 正式交付4和反向3；坏来源理由仍交付4；坏客户/单位/来源/SKU/日期、孤儿、软删头/软删已过账行、原始单位前导空格计数防线。
- Guard 对相关整头、历史 Link、已删除 scope 行、同头其他坏行与无关坏历史的区别；空/软删 Gross 头仍先 Guard；真实服务消息和 Guard 后事务仍可继续。
- 无外层事务、预取消、多个空集合、返回后连接及调用者事务可用、无临时资源。
- 七个阶段开始时使用**测试专用真实命令包装器**执行同连接/事务的只读 pg_sleep，等待命令已发起后取消；不改业务 SQL/参数或在生产添加回调。取消后明确由调用者回滚再验证连接复用。这个屏障证明阶段命令边界的真实驱动取消，不冒充业务 SQL 自身锁等待或跨连接快照更新验收。

按分派本窗口未连接运行上述场景，也未运行源只读探针、实库 Guard、PG 资格或任何数据库/DDL。源码与测试编译不能证明实际表列、数据库条件、错误传播和并发通过。主控真实执行建议使用分类/`RealPg_` 过滤并启用严格 FAILURE 门禁，其他开关清空；本窗口没有执行此命令。

## 必须保留的造数边界与主控事项

1. 元数据 `UX_SupplierProgress_Line_Sequence` 对同采购行/Sequence 唯一，无法合法 INSERT 同序号两报告验收 latest 尾键。本轮保留生产尾键和离线实际调用检查，采用合法 Sequence1/2 和最新NULL的场景；主控已完成的 UUID 协议探针可复用，但**不是最新报告并列的完整 Trace 实库通过**。不能禁用唯一约束；如还需并列场景，由主控安排合法隔离方案。
2. `CK_DemandExecutionLink_Target` 只允许 PO，`CK_DemandExecutionLink_NotDeleted` 禁止软删。非 PO Shared 和软删 Link 无合法当前夹具，本轮未造数/绕过 CHECK。生产仍保留原 Shared 无 TargetType 追加条件、scope 无 IsDeleted 条件；用合法 PO Shared 与完整释放历史 Link 验证可构造行为。负数量/非法 Link 状态同样不绕过 CHECK。
3. 真实跨连接阶段间更新、ReadCommitted/RepeatableRead 的快照边界及 SQL Server 锁式扫描差异本轮未验。若需可提交两连接夹具，按冻结要求先由主控登记独立目标/精确随机 UID 清理，不在现有回滚夹具中静默提交。Material 覆盖6/1及新展示量由 A/主控归属，本窗口没有接管。
4. 当前主文件仅 Trace，不注册 DI、不切工厂。完整 Gross 接口其余三个查询仍由后续分派完成，不以本轮通过数提升整个仓储或 M14 完成状态。

## 共享依赖与既有改动保护

三份主控停写同步依赖本轮只读，前后 SHA 相同：

| 依赖 | SHA256 |
| --- | --- |
| PostgreSqlInboundReceiptHistoryGuard.cs | C6578ADD901BBDE13B8178A06A5AB00CE0126C2D938AE534FFF8BE0C703E291C |
| UnverifiedReceiptHistoryException.cs | 574BE8712E8C62DD0E05AB063F517012F82E2A1B80F9C79E0040BF855A5B9234 |
| PostgreSqlExceptionClassifier.cs | 8C4F2808D2B6F1267490C142099C69C3ACD7D6C424E8051890602A0DC3E5C84F |

工作树原有 RBAC/执行资格同步修改及此前 M12/M13/M26 成果保留，不能整体同步/回退或把这些既有文件算成本轮写入。未输出连接串或凭据；未提交、推送、部署、创建聊天或代理。结果先写，本人状态随后 STOPPED，最终返回绝对路径与真实命令/指纹。
