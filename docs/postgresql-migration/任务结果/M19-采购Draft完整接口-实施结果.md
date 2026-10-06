# M19 采购 Draft 完整接口实施结果

轮次 M19-DRAFT-COMPLETE-20261005，执行 A。主 Agent Backend，后端API工作流，复用已读 Dapper、会话、审计及质量技能。事实来源 MAIN `D:/Documents/source/CSharp/SwiftExport` 当前原仓储、完整接口、PurchaseDraftCreationService 及 WriteModelBuilder。工作区 `C:/Users/BigBigBigDv/.codex/worktrees/9b64/SwiftExport`。**完整八方法代码完成，一次编译通过，实库与完整创建/追加联合协议待主控验收**。

## 交付边界与固定 SHA

只修改原一个文件 `SwiftExport.Infrastructure/Repositories/Procurement/PurchaseOrders/Draft/PostgreSqlPurchaseOrderDraftRepository.cs`：声明 IPurchaseOrderDraftRepository、更新职责摘要，补六方法及必要私有辅助。SHA256：`85962553E9582AE1EF8FBF77EB8426DBFA3F7D8E719B72619D3AA4643F10BA8B`。

另新增本报告、更新本人 `窗口状态/执行A.md`；报告自身SHA见STOPPED状态。不新partial/仓储文件/接口/实体表/租约/共享锁/事务门禁。前面各批归主控冻结，不续改或重写。没有Git/提交/推送、整树同步、子Agent、新工作区、数据库连接或部署。

修改的是 Infrastructure 持久化 SQL 实现，未改业务规则。源 SQL Server、服务/Core/DTO/DI/测试、共享参数/元数据/审计/会话及其他旧成果只读。新技术失败边界见下文，不把编译当作运行接线验收。

## 已交付两个普通读取及 helpers 冻结

GetSafeAppendCandidatesAsync、GetPurchaseDraftsByIdsAsync 的方法、原44列HeadFields与元数据、SafeFacts、Table/C/Dictionary/UuidOrder/SourceParameters 均未重写。两普通读取整段与原字符内容相同；从原 SafeFacts 职责摘要至文件末尾的既有 helpers 整段也相同。开始时本文件与 MAIN 同 SHA `A164181A8D8923D4BB41DEA66F3483527AE1761EC9D4D0FF111F23E51ECDBBE2`，符合此前冻结成果。

## 六方法及原语义

|方法|实现与原行为|
|---|---|
|GetByExecutionScopeForUpdateAsync|原四项身份/文本入口校验及顺序；SourceType .Trim()；按 ExecutionRequestId、DemandSourceType、SourceUid、SupplierId、未删资格读取原44列，锁实际头 FOR UPDATE。没有追加OriginKind、活动/确认状态或非空执行身份规则；缺头返回null，不声称ExecutionScope空范围保护。|
|GetSafeDraftForUpdateAsync|Uid非空、ExpectedVersion正数校验；原版本、活动、待确认、来源文本非空、来源Uid/供应商/ExecutionRequestId非Empty、未删与两项安全事实谓词保持；原NULL比较不补Empty/default。读44列，实际头及安全事实查询中的实际行FOR UPDATE。|
|InsertPurchaseDraftAsync|null头先拒绝，Empty Uid先拒绝；保留Application稳定Uid；StampForInsert之后IsDeleted=false，仅Version<=0时改1，正版本保留。原45固定字段插入，返回影响行数，无泛型Add、额外身份/生命周期校验或事务守卫。|
|GetMaxLineNumberForUpdateAsync|Empty采购头Uid拒绝；只计头未删且行未删的LineNumber，含零/负值原样聚合，空集返回0；先物化并锁实际明细，再MAX，不对聚合结果非法FOR UPDATE，不追加来源/状态规则。|
|InsertPurchaseDraftLinesAsync|null集合拒绝；ToArray一次，空集返回0；在任何打戳前校验全部行非null且Uid非Empty；随后所有行依次打戳、清软删，非正Version置1。按原输入顺序逐行，单语句owner CTE锁实际未删父头并INSERT原37固定字段，缺父头不写此行且立即抛失败；不补头状态/来源/版本规则。|
|AdvanceDraftVersionAfterAppendAsync|原Uid/ExpectedVersion入口校验；乐观版本、活动、待确认、未删及安全事实谓词；先锁实际合格头和未删正Quantity明细，再MIN(PlannedDeliveryDate)重算ExpectedDate，无日期返回NULL；后台Now/UserName与Version+1直接SQL更新，返回影响行数，不回写调用方头实体。|

头45、行37固定插入**参数属性名及顺序**与 MAIN 原 VALUES 全量比对一致。读取44列仍不增加OriginKind等字段。原SQL Server QTY 的参数为 @Quantity，在PG使用既有实体映射 Quantity 物理列，无新增名称规则。所有写入共享 PostgreSqlParameters.Entity，保留NULL、整数枚举和既有timestamp/timestamptz绑定；Advance直接时间用既有AddTimestamp，不另取审计服务或改变原Now时机。

所有方法借用会话/可空Transaction；原仓储没有新增方法的额外事务守卫，本轮没有强加守卫/事务/租约。新增六方法原无CancellationToken参数，未改接口或代入服务令牌。插入异常/缺头/后行失败后，原打戳、IsDeleted和Version副作用仍已发生；之前成功写入由调用方事务回滚，不在仓储撤销内存状态。

## 安全事实与PG锁边界

SafeFactsWithLocks 与原 SafeFactsPredicateWithLocks 保持两项资格：

1. 未软删入库行（NULL IsDeleted视false）若SourceLineId指向该头任何采购行，则不安全；采购行身份范围包含软删行。
2. 任意库存流水通过入库行回指该头任何采购行，则不安全；流水及中间入库行不加软删/状态过滤，反向和历史事实仍阻断。

只在新锁定方法内使用新 helper；原普通SafeFacts不动。IN范围的采购行、匹配入库行，以及流水INNER JOIN的两实际侧使用FOR UPDATE。没有新增下游事实类型/方向、父头业务判定或全局锁。

MAX/MIN由锁定原始行的MATERIALIZED CTE供聚合；Advance的candidate CTE锁合格头并检查原安全谓词，locked_dates读未删正数量行，最终版本谓词再次约束更新。缺采购行/入库/流水范围没有可锁元组，不能等价SQL Server HOLDLOCK；子查询执行计划、锁序、旧快照及来源/入库联合变化由主控实库验收。没有擅自扩大公共协议或减少隔离级别。

### 缺父头失败的技术差异

原 InsertPurchaseDraftLines 在 SQL Server 执行 THROW 51000。PG用同一INSERT语句中的owner锁定CTE确保无父头不写行，受影响行数0时抛 `InvalidOperationException("采购单不存在或已删除，不能追加采购行。")`，而非伪造SqlException/Npgsql服务端异常或新增数据库函数。**异常CLR类型/提供者错误号未原样迁移**，单独登记待主控确认；缺头仍失败、停止后行且消息相同。

真实唯一创建/追加调用方 PurchaseDraftCreationService 在通用catch(Exception)中回滚事务和结果缓冲，返回原 `UNEXPECTED_ERROR / 创建采购草稿失败。`；没有针对51000/provider类型的分支。因此现有服务失败结果保留，但不能宣称所有潜在外部消费者的提供者异常合同已验证。当前未部署或注册，不借此扩展共享错误分类。

## 真实创建/追加调用链

MAIN PurchaseDraftCreationService 每来源先进行已存在的物料/来源/命令项准备，再开启原默认事务；锁序注释为 GrossDemand -> NetDemand -> Link/CommandItem -> PurchaseOrder。多个供应商按.NET SupplierId排序在同来源事务写入。

新建：查ExecutionScope重复头 -> 取号/Application生成稳定头Uid -> WriteModelBuilder生成OriginKind1、Version1及稳定行/Link身份 -> 插头 -> 插行 -> 插Link。追加：GetSafeDraftForUpdate(uid,expected) -> ValidateAppendHead -> MaxLineNumber+1 -> 原Builder行模型 -> 插行 -> 插Link -> AdvanceDraftVersion(uid,appendHead.Version)。仓储不接管取号、Link写入、业务回执或来源资源准备。

Advance成功后服务报告版本为appendHead.Version+1，原appendHead实体不自动Version++。WriteModelBuilder/结果缓冲事实保留：单来源多供应商原子、来源间允许部分成功、提交后PublishCommitted，错误/取消回滚和Rollback缓冲；不把整批改原子或自动重试。

## 验证与主控待验

运行一次：

```powershell
dotnet build ./SwiftExport.Infrastructure/SwiftExport.Infrastructure.csproj --no-restore -v:minimal
```

成功，0警告、0错误，2.39秒。既有NuGet资产可用，无恢复/安装或重复隔离构建。静态检查完整八接口、原方法/helpers冻结、45/37参数全量顺序、44列锁定投影、资格、打戳/版本/父头失败及真实服务消费；新增长段全文检查。

按分派未新建/运行普通测试、全量、SQL Server或PG场景，没有DB/DDL/DI接线。查看既有PG普通读取测试和Append SQL/创建编排相关入口。所查普通读取测试未发现明确禁止完整八接口的断言；原读取断言有效，不改测试，集中阶段由主控执行并判断阶段门禁。

待主控至少验：

- 原两个读取回归；ExecutionScope全部校验/CI输入/多结果异常/缺头与真实新建幂等联合场景。
- SafeDraft所有身份/版本/安全资格，含软删采购行、NULL软删入库、软删历史中间行/正反向流水；FOR UPDATE子查询的PG语法、计划和实际行锁。
- 45/37插入NULL/时间/枚举往返、稳定Uid、正版本保持和非正置1、先全体打戳后SQL副作用；重复身份/缺父头/后行失败及源默认事务全回滚。
- MAX含负行号/软删/缺头/空行；Append实际Max+1顺序和并发追加，不把空CTE当范围锁。
- Advance预期版本/状态失败返回0，最早交期排软删及非正数量、NULL日期，Version+1和后台时间；头实体不回写，结果缓冲提交后发布。
- 缺ExecutionScope范围、高隔离首次快照、空下游事实及入库/库存/来源同时变化；全创建/追加/命令重放、准备/事务释放与回滚异常归主控。不能仅本仓储编译宣称幂等、范围或联合锁协议通过。
- 确认51000到CLR失败的提供者类型差异是否需共享统一分类；任何跨文件修复归主控，不在本独占文件私自新增框架。

## 起止采样与既存差异

12项 MAIN 起止SHA均相同；本地仅授权 PG 仓储变化，其余采样保持原样。按MAIN事实判断，使用本地依赖编译；既存差异未被整树同步覆盖，不等同MAIN全链构建已通过。没有读取或修改连接字符串；UTF-8仅写授权文件、报告和本人状态。

|路径|MAIN起止SHA256|本地起SHA256|本地止SHA256|
|---|---|---|---|
|SwiftExport.Infrastructure/Repositories/Procurement/PurchaseOrders/Draft/PurchaseOrderDraftRepository.cs|`62DAB28842997F050A583E0E295FDCF80A38101E65470A9CCDE30ED9D4239D22`|`41F6B59A0B6F2200AE18237D51A839681985C0406EB6005D0BF2644FF781B119`|`41F6B59A0B6F2200AE18237D51A839681985C0406EB6005D0BF2644FF781B119`|
|SwiftExport.Infrastructure/Repositories/Procurement/PurchaseOrders/Draft/PostgreSqlPurchaseOrderDraftRepository.cs|`A164181A8D8923D4BB41DEA66F3483527AE1761EC9D4D0FF111F23E51ECDBBE2`|`A164181A8D8923D4BB41DEA66F3483527AE1761EC9D4D0FF111F23E51ECDBBE2`|`85962553E9582AE1EF8FBF77EB8426DBFA3F7D8E719B72619D3AA4643F10BA8B`|
|SwiftExport.Core/Interfaces/Procurement/PurchaseOrders/Draft/IPurchaseOrderDraftRepository.cs|`0EC233B5A2E63DAB53062483CDEE9FC2B349111D0F32C2ECC921E4FB20A55246`|`C86BCD00F525905178D701BCB5506832ECA66F825CC5AC27267470BDA485D30A`|`C86BCD00F525905178D701BCB5506832ECA66F825CC5AC27267470BDA485D30A`|
|SwiftExport.Core/Entities/Procurement/PurchaseOrderHead.cs|`6737032BB179F3E751D8D0F1727853B867DB40613A85A08C7A6A87FF60C9CB4A`|`6737032BB179F3E751D8D0F1727853B867DB40613A85A08C7A6A87FF60C9CB4A`|`6737032BB179F3E751D8D0F1727853B867DB40613A85A08C7A6A87FF60C9CB4A`|
|SwiftExport.Core/Entities/Procurement/PurchaseOrderLine.cs|`C3B61698EBDC6345BDEE2BB159641AE622A14E9F6482271AAF53FA2767F8A992`|`C3B61698EBDC6345BDEE2BB159641AE622A14E9F6482271AAF53FA2767F8A992`|`C3B61698EBDC6345BDEE2BB159641AE622A14E9F6482271AAF53FA2767F8A992`|
|SwiftExport.Core/Entities/Inventory/InboundDocumentLine.cs|`67978D5BD53530F775964D5392EB7B4CF1FDE712C3C11BDD07CF195AD85A553C`|`99897DE5BF06E835CFC6BAFDEDAFE13F2EFA12241D8B603CA555F9BE3BE7C219`|`99897DE5BF06E835CFC6BAFDEDAFE13F2EFA12241D8B603CA555F9BE3BE7C219`|
|SwiftExport.Core/Entities/Inventory/InventoryLedgerEntry.cs|`CDBE7658A9D1A325D7725C3A17012A8D85AE93155254C5FCECF166ECEDF45231`|`02D2110B01B3BEA673148F1DD699F3094B7090E0D272E1C274654D3B991C6951`|`02D2110B01B3BEA673148F1DD699F3094B7090E0D272E1C274654D3B991C6951`|
|SwiftExport.Application/Services/Procurement/PurchaseOrders/Draft/FromDemand/PurchaseDraftCreationService.cs|`EEA6AA3347F81A39A1BB5E0E45A1F1B65B5E94993E48BA8670BF2010B01085A8`|`F438D1973331F160AAE511A5E42F19BB7306A504213C5CC239237E2C8B38602C`|`F438D1973331F160AAE511A5E42F19BB7306A504213C5CC239237E2C8B38602C`|
|SwiftExport.Application/Services/Procurement/PurchaseOrders/Draft/FromDemand/PurchaseDraftWriteModelBuilder.cs|`2473C6DD9121B58681244947D734C70A3199F2296BCAAD66C41D6F6F0BFEAC98`|`A264FD554FFA87E90992D5BD4AF97BF0A6EA97F30EFD96E7670C4933A9992707`|`A264FD554FFA87E90992D5BD4AF97BF0A6EA97F30EFD96E7670C4933A9992707`|
|SwiftExport.Infrastructure/Repositories/Base/PostgreSqlParameters.cs|`AFD137139BE722A82E53BB28EFB26FF2684DF5FD9CB00140E631C484CD33B57A`|`AFD137139BE722A82E53BB28EFB26FF2684DF5FD9CB00140E631C484CD33B57A`|`AFD137139BE722A82E53BB28EFB26FF2684DF5FD9CB00140E631C484CD33B57A`|
|SwiftExport.Infrastructure/Repositories/Base/PostgreSqlEntityMetadata.cs|`1C025B0FE154139E4D7B3B81913A2997214FF967ABDAE40EC62BFCDDFF89F064`|`1C025B0FE154139E4D7B3B81913A2997214FF967ABDAE40EC62BFCDDFF89F064`|`1C025B0FE154139E4D7B3B81913A2997214FF967ABDAE40EC62BFCDDFF89F064`|
|SwiftExport.Infrastructure/Services/Auth/AuditStampService.cs|`7E82ACA804F21F7E8308D0D2F4040BBEB47BE71B7294371A39128DCA75688525`|`7E82ACA804F21F7E8308D0D2F4040BBEB47BE71B7294371A39128DCA75688525`|`7E82ACA804F21F7E8308D0D2F4040BBEB47BE71B7294371A39128DCA75688525`|

交付未提交，固定SHA供主控精确接回。

