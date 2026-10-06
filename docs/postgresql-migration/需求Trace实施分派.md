# M14 Trace实施分派（A/B各一个独立归属）

2026-10-05；用户已授权主控与两个现有worktree继续迁移。两窗口上一轮审查本人STOPPED且聊天completed，结果精确接回；共享历史Guard、Core专用异常和PG classifier三依赖已按固定旧/新哈希同步，保持只读。必须先读[Trace读取协议冻结](Trace读取协议冻结.md)、实际源Trace和本轮相关接口/DTO/服务/测试，已读规则技能复用。源SQLServer语义优先，不扩业务/API/结构。

## A：M14-S03-T01-IMPL-01 Material Trace

允许修改本人已有PostgreSqlMaterialDemandQueryRepository.cs，仅为完整现有IMaterialDemandQueryRepository接入加using/接口声明及必要共享私有衔接，不重写已验两工作台方法。新增同目录PostgreSqlMaterialDemandQueryRepository.Trace.cs、Application.Tests/Database/PostgreSqlMaterialDemandTraceMigrationTests.cs及.Integration.cs、本人任务结果/M14-物料Trace-实施结果.md和状态。其他旧源码/测试/结果/主清单/公共依赖只读。

完整第三方法GetTraceAsync遵循scope→Guard→header→tree（缓冲）→空头null分支→一次覆盖SQL→一次采购展示SQL→原C#汇总。原GetSourceReviewReason逐字行为保留：Ordinal MD/指纹、IsNullOrWhiteSpace、associated超量仍保留数值/coverage未知；不改DTO。冻结覆盖关系用等长UUID/UUID/decimal?数组与显式PG cast/unnest，empty/NULL保留；覆盖计算仍SQL eligible/ranked及单位/收货子查询，不用C#重算。不刷新旧覆盖、不将起始Guard范围代替后面的Link范围，不额外再Guard。窗口无拆解行号，日期/来源行号NULL最后/源UUID尾键及ROWS保留。

离线真实公开调用证明各阶段顺序、单次覆盖/冻结数组、范围/NULL/DTO、可空/已有事务及token；真实PG场景代码覆盖6+6/7与并列尾键、版本/指纹/客户/单位/目标/历史/超量、报告Sequence并列源Uid DESC、相关整头/无关坏历史、空头含软删头、已有事务回读/返回后可继续、预取消及相应原服务错误。可编写真实命令包装器控制阶段，但不加生产测试回调/通用框架。数据库由主控执行，不在本人窗口连接。

本任务完成后完整Material接口可编译，但仍不注册DI/切工厂；工作台30离线/50实库已有证据不等于新Trace通过。原工作台代码若确需越界行为修改先报告主控。

## B：M14-S02-T01-IMPL-01 Gross Trace

允许新增DemandPlanning/Sources/Query/PostgreSqlGrossDemandQueryRepository.cs及.Trace.cs、Application.Tests/Database/PostgreSqlGrossDemandTraceMigrationTests.cs及.Integration.cs、本人任务结果/M14-来源Trace-实施结果.md和状态。主文件仅sealed partial构造既有context及Trace所需私有映射/参数，当前**不声明完整IGrossDemandQueryRepository、不加其他三方法占位/SQLServer委托/DI**。主文件/Trace未来保持同一归属；其余三个方法后续独立分派。

真实Trace按scope→Guard→header→materials（缓冲）→空头null分支→purchases→deliveries→非法箱单计数→原汇总。保留历史/删除物料、shared无TargetType附加筛选等Gross与Material原差异；source理由独立于delivery理由，来源指纹错误仍可DeliveredQty=4，原装箱检查才让交付未知。反向箱单展示正量但不计正式净量；孤儿/软删过账/原单位比较由独立计数挡住。Purchase反馈整采购行、不分摊，Sequence相同采用源Uid DESC尾键；各显式DTO别名/PG quantity及参数类型核验。

真实场景代码必须覆盖上述原业务条件、多个集合空值/历史/无头、各坏历史/缺目标/装箱客户单位及源理由不影响确定交付、报告并列、可空事务与调用者自写/保持外层事务、取消和Guard原服务消息（如用测试适配器仅转发真实GetTrace，不能声称其余接口已实现）。原始LIKE不在该切片，B07/B10不开样例修复。

## 两窗口共同门禁

各自仅必要受影响编译/本轮独立离线测试，清四个PG及SOURCE_READ开关；禁止全量测试、实库/DDL/重置、Git提交推送、开启新窗口/代理。源和新PG门禁都需看实际调用链，不仅匹配字符串；模拟结果不算PG SQL通过。真实测试使用现有严格故障库门禁、随机实体/UID及一条ReadCommitted夹具事务finally回滚，INSERT按已有元数据保留指定UID/软删，不用通用Add重置历史关系、不绕过CHECK。需要提交跨连接夹具或共享文件先报告主控，由主控安排隔离目标/精确清理。

先本人状态运行中，关键边界/失败更新；先结果→停写→最终答复绝对路径和固定SHA、真实命令/结果/未运行事项。主控仅停写核验后精确接回获分配文件，A/B不互改文件/主文档。服务/接口/DTO/公用元数据、guard/classifier及共享结构均主控所有，发现必要改动先报告，不自行扩大。
