# M18-Q03-IMPL01 采购Trace实施

2026-10-05，现有执行A/9b64。详情STOPPED/completed后固定SHA接回，前轮全部文件写权归主控；本轮Backend / 后端API工作流、既有仓储/会话/质量技能。

仅新增 `SwiftExport.Infrastructure/Repositories/Procurement/PurchaseOrders/Trace/PostgreSqlPurchaseOrderTraceRepository.cs`、`SwiftExport.Application.Tests/Database/PostgreSqlPurchaseOrderTraceMigrationTests.cs`及`.Integration.cs`、`docs/postgresql-migration/任务结果/M18-采购Trace-实施结果.md`，另本人状态。实现原IPurchaseOrderTraceRepository.GetAsync完整方法，不接DI。公共Guard/元数据/参数/源/Core/DTO/服务/所有前轮交付只读，不DB/DDL/全量/Git/代理聊天/跨聊天消息。

事实源主仓库PurchaseOrderTraceRepository（SHA4ADFBF342C387073615BF1E86FBBBD863AB67AC81AFDC968F9D4118AA3E3AA80）、实际接口/DTO/服务及M18已读审查。新查询真实依赖P02/P03和M14-P02 ScopedGuard，非M15写仓储。共享Guard SHA7F5964503D8FB7BA35DFE2BA2C7941C3D7EEE16F3ADA94D93D589D03AB96925F。

源临时scope最小替代：一次SQL缓冲本采购头全部行UID（含软删，不要求有效头）→同Session可空事务ValidateScopedAsync一次→一次QueryMultiple发送原六语句（有效头、全部历史行、Link、反馈、正式入库、操作日志）。头null按原早返Dispose，其ReadSingleOrDefault会推进行结果；不能改为显式缓冲全部尾集合、跳过scope/Guard或加事务。正常依原顺序全缓冲后原C#组装。PG缺头推进/释放及服务器阶段变化须实库验证，驱动若不能等价先报主控。起始scope仅给Guard定位整入库头，六结果后续都重新按当前表PurchaseOrderId查询，不能用scope数组冻结后续集合，不能加第二Guard。

保留全部历史行/Link/Net/Gross，不增生命周期/删除过滤；当前Product仅未删。Link以TargetLineId对应本单行、TargetType PO，不丢错头证据。版本是SourceDemandVersionSnapshot==当前Gross.Version；MD来源版本双方均NULL，非MD双方非NULL且相等；原SQL NULLIF空串及字典指纹比较，不借销售64位格式/.NET指纹算法。单位当前Product.Unit ASCII trim，缺时仅根Net回落Gross.Unit，不优先Net.UnitSnapshot。保留目标头身份、贡献/释放量、原因CASE优先；不能把CHECK禁止的非法旧值绕约束造夹具。P03/B10/B19 Unicode比较前置仍开放，沿当前已验字典/尾ASCII规则且如实登记，公共修订由主控，不擅自全局normalize。

当前行LatestReadyQty按SQL整行Sequence DESC/源SqlGuid DESC，与后面完整反馈历史独立观察，不能用稍后History首项覆盖已缓冲值。正式收货Posted贡献Quantity、Reversed贡献0，只按行身份不Link分摊；全部原排序NULL方向与SqlGuid保留。原C#单位IsNullOrWhiteSpace或!=是.NET字符串比较，不能换成SQL字典比较；不一致使行FormalReceivedQty保持原NULL并覆盖ReviewReason，否则原Sum（空集合0），可保留其他已有ReviewReason。OriginKind2无Link不造缺来源错误，非2有效无Link及NeedsReview保持原中文。操作日志不加Module过滤，按AggregateUid/未删/OccurredDate DESC/SqlGuid DESC。

完整quoted aliases、Quantity物理quantity、显式nullable参数/uuid/int[]，无测试钩子/临时实体/JSON框架/新的锁或持久对象。只独占离线及必要编译，清全部PG/SQL源开关。真PG只编写编译，固定failure DoNotParallelize、合法随机RC finally回滚，无初始化/提交/清库/DDL/禁约束。覆盖scope含删行/整头坏历史/无关历史不拒绝、缺头/软删头早返连接可用、独立无Link/历史软删、当前单位根回落/版本/指纹/理由、最新整反馈与独立History、Posted/完整Reversed、日志排序、原C#单位区分及可空事务/取消。服务器中途变化/物理跨连接未运行明确记录；先RUNNING，先结果后STOPPED后固定SHA交付。
