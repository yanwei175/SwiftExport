# M03-BOM-REVIEW01 共享 BOM 保存与真实需求读取审查

## 结论与范围

No findings. 本轮没有取得可证明由本次迁移新引入的具体回归证据；这不表示共享写入及真实需求 PG 链路已全面验收。任务是当前实现审查，不提供 Git 比较基线，且明确禁止 Git、编译、测试和数据库访问，故不把历史问题或测试缺口包装为新回归。

主 Agent：Reviewer；Workflow：代码审查工作流；采用 review-agent，复用 Dapper、会话、命令、质量规则。事实来源为 `D:/Documents/source/CSharp/SwiftExport`（下文 MAIN），不是 9b64 旧副本。仅新建本报告及更新本人状态；M19 四份成果已归主控冻结，本轮没有续改。未改业务代码、项目、公共文件、测试、规则或旧报告，无 DB/DDL/构建测试/Git/同步/代理/跨聊天消息。

已有共享保存、产品绑定边界、PG BOM 完整接口及真实需求按共享编号读取，不需要重做迁移。最小剩余工作是以当前源码组合真实 PG 仓储，补空范围竞争、产品绑定/角色与组成竞争、保存后真实需求读取、旧快照及失败原子性验证。生产 `IBomRepository` 仍注册 SQL Server，不应仅换工厂切 PG。

## 当前可达调用与方法合同

定位依据：MAIN `SwiftExport.Core/Interfaces/IBomRepository.cs`；两提供者 `Repositories/Products/{BomRepository,PostgreSqlBomRepository}.cs`；`BomService.cs` 及其兼容写入分部；`ProductMaintenanceService.cs/.SaveBoundary.cs`；`MaterialDemandEstablishmentService.cs`；Controller、BaseService 及两层 DI。应用、WebApi、Infrastructure 中搜索 IBom 注入、具体方法调用及旧 Reader 名称。

真实需求路径是 `MaterialDemandEstablishmentController → MaterialDemandEstablishmentService.EstablishAsync → CommandPipeline → BusinessCommandExecutor → BuildPlanAsync → GrossDemandDecompositionPlanner`。`BuildPlanAsync:187–208` 先锁真实根，按其正标记和非空编号调用 `GetBomByBomNoAsync`，再锁直接子产品并验证客供终端；不读取旧数值版本。Planner 只生成标准根及直接客供件，数量为需求量×分子÷分母，普通子件不转成递归采购链；非标只生成一个根。物理来源父和编码快照不回写为实际根。

`IBomExecutionReader/BomExecutionReader/IBomExecutionRepository` 在本轮应用/WebApi 调用及 DI 检索中没有生产消费者，只有声明、旧实现和测试定位；当前 DI 也未检出其注册。它不进入本次 PG 完成标准。采购确认不恢复旧完整 BOM 读取/Execution 链。

|方法|实际可达消费者|SQL → PG 当前合同与投影|
|---|---|---|
|GetSharedSaveFactsAsync|BOM 保存；产品保存结构边界|非空事务必需；请求数组保留下标、重复及原文，空请求仍读全图。全部未删组成、未删正标记且非空编号产品、输入空组以一条 SQL 汇总；数据库确定编号等值组，不按标记数值分组。SQL 保存默认 UPDLOCK/HOLDLOCK，产品显式关闭；PG 保留参数但无全图 FOR UPDATE。三类型全字段别名，Uid 排首，读取器按临时 GroupKey 汇总一次，不复制组成。|
|GetBomByBomNoAsync|真实需求；保存按号删除准备；校验和树展示|输入 Trim；未删行且行标记>0，存在任意未删正标记同号产品；来源父身份、IsActive、正标记数值相等不参与。完整17列组成实体，LineNumber/Uid 排序；复用外层事务，不加读锁。|
|GetBomDtoByBomNoAsync|BOM by-no HTTP 查询|同一共享有效资格；17个 DTO 别名，组件编码取存储快照，中文描述/配置/图片及颜色取当前未删关联；LEFT JOIN 保留关联缺失行，展示字段可 NULL；不按停用过滤。|
|HasActiveBomForProductAsync|真实需求直接客供子件终端检查|产品 UID 或 Trim 后候选编号对应真实未删正标记共享引用者，并存在未删正标记组成；空白编号转 NULL，孤立组成不能被编号激活；不依赖来源父。返回 bool，无锁。产品保存已不拿该方法代替候选完整图边界。|
|GetPagedAsync|BomService.GetPagedListAsync 服务契约|共享有效资格、DTO投影及两个结果集计数/页；页码>=1，默认20、上限500；十字段关键词及三快照批量词，后者去空/Trim/OrdinalIgnoreCase 去重/最多300；PG TEXT[]+EXISTS，重复等值输入不放大。排序编号/行号/Uid；SQL NOLOCK 不变成 PG 业务锁。当前 BomController 无专门分页 Action，不虚构 HTTP 可达性。|
|GetByBomNoAndVersionAsync|本轮未检出生产调用；历史核验测试|空白/非正直接空集合；Trim 后编号+指定数值+未删，不要求父存在/当前标记；完整17列。保留兼容接口，不回接真实需求。|
|GetCurrentReferencesForProductsAsync|本轮未检出生产调用；专门测试|NULL/空/仅Empty免SQL；去Empty和重复UID；当前有效共享父或组件命中，EXISTS避免物理行放大；完整17列，不新增锁。产品保存实际使用全图共享事实。|
|GetBomsCollectionByBomNoListAsync|本轮未检出生产调用；专门测试|原输入单次枚举，不Trim；NULL/空免SQL；读取全部历史与软删，无父资格；完整17列，数据库等值筛选后按原存储文本精确字典分组，不把ICU等价键合并。|
|IsBomItemExistAsync|本轮未检出生产调用；专门测试|原编号/子件编码，不Trim、不筛软删/版本/父；SQL COUNT→bool，PG EXISTS→bool。|

PG 固定文本比较使用 `rtrim(...,' ') COLLATE public.erp_dictionary_ci`，只兼容尾部 ASCII 空格。等值数组不重新用 C# 判断数据库组归属。查询中的 PG 原生 uuid 排序没有改成 SQL Server SqlGuid 权重；现有分页实测断言采用 CLR Guid 顺序。若后续要求跨提供者完全相同行顺序，应先冻结合同并补特制 UID 对照，不能以当前测试替代该证明；此处不调整现有排序语义。

继承的泛型接口同样已实现，实际 BOM 链路使用 `GetByIdAsync`（BaseController/BaseService详情）、`GetAllAsync`（显式服务）、`GetByIdListAsync`（保存）、`Add/Update/DeleteAsync`（统一保存内核）。`ExistsId/GetExistingIdList/GetSingleEntity` 在本轮未检出 BOM 服务直接调用；IBom 继承完整不等于每个成员都有生产消费者。泛型按UID读取未删实体，GetAll未删并按更新/创建时间倒序；请求更新锁时 PG 只锁已返回现存行且须有父事务，空 UID 集合免SQL，不保护不存在范围。

## 保存、实体回写与共享规则

当前 `BomBatchSaveRequest` 是增改删差量内核，不是按编号 DELETE ALL 再重建，也没有组级 ReplaceAsync、聚合 Version 或 CommandId。所谓“整组”指完整最终候选图校验与批量原子性；不能把需求解释为新增替换接口。

`BomService:207–317` 拒绝已有事务，自建 Serializable 并独占提交/回滚/释放。按号删除在事务内先解析当前组成及行 Version，随后仍走完整图内核，不能绕过“有效父不得留下空组成”；历史行不一并删除。`PrepareBatchSaveAsync:341–473` 顺序为：请求校验→锁请求增改删目标中的既有行→行Version检查→共享全图/输入组→父资格/异常标记→定点锁父和组件并核对父Version→构造候选→完整图校验→写入。

新增行使用共享组编号、有效标记1、确定来源父及编码快照、组内现有最大行号+1；同物理行只写一次。修改先复制旧实体，保留编号、正标记、来源父/快照、原创建审计和行号，仅替换组件身份/当前组件编码快照、分子分母和规范化备注，wrapper UID/Version 为权威。DTO BomNo Trim；空白拒绝。已有行不得移组；非正旧行拒绝，孤立旧组成不借绑定激活。不同正数都有效，仍无业务多版本。

两提供者泛型写列对照：

|操作|写列、回写和返回|
|---|---|
|Add|17个映射列全插入；AuditStamp 填缺失创建/更新审计；强制IsDeleted=false、重新生成Uid、非正Version补1，直接回写传入实体；行数1返回RepositoryWriteResult成功。PG参数按实体元数据绑定。|
|Update|除Uid/Version外15列更新（含创建审计；服务候选保留旧值），数据库Version+1；更新审计强制刷新；Uid+OriginalVersion+未删谓词。调用前即把实体Version设原值+1，即使零行也已回写；SQL OUTPUT/PG RETURNING 返回新Version；零行CONCURRENCY_CONFLICT由BOM服务转VERSION_CONFLICT。不得静默改成失败不回写。|
|SoftDelete|只UpdatedDate/UpdatedBy/IsDeleted；Uid+Version+未删，返回原Version，不递增；不是补Version功能的授权。|
|HardDelete|Uid+Version物理删除；成功新Version=NULL；保留失败码及消息。|

业务成功回执保持请求ClientRowId/RowIndex及正式身份版本。任一行失败整批回滚，停止后续写；服务成功只在Commit后返回。明确序列化失败（语句或提交）分类SERVICE_UNAVAILABLE且无自动重试；实体冲突独立VERSION_CONFLICT；其他提交异常OUTCOME_UNKNOWN，不能据后续回滚宣称未提交。普通BOM请求没有CommandId，不提出“原CommandId重放BOM”的方案。

`SharedBomStructureValidator` 检查受影响组全部共享父的准入、比例、自引用、重复组件/行号、非空结构和可达循环；来源父不当独占所有者。合法不同分支共享组件不误判为环，未受影响坏组不冻结所有资料操作。纯资料更新且结构字段精确一致可跳过全图；产品新增可先绑定全新空编号，绑定既有组成则验证候选图，已有有效绑定禁止普通换组/关闭。

## 连接、首次快照及等待边界

仓储与 Application 通过同 scope DbSession/IDbSession共享同一物理连接和Transaction；仓储不提交业务事务。PG具体类目前主要通过测试组合根组装；生产 Infrastructure DI 仍 `IBomRepository → BomRepository`，不是PG宿主完成。

|路径|实际顺序与保护|剩余证明边界|
|---|---|---|
|BOM 保存|自有Serializable。更新/删除先锁目标BOM行再读全图；仅新增时目标集合为空免SQL，共享事实可成为首次数据快照；按号删除先普通当前读取。共享事实无PG锁，之后父/组件FOR UPDATE，再逐行写组成。|没有BomNo租约/空范围行锁；SSI及数据库约束承担后备。UID参数OrderBy不保证数据库实际锁顺序，基类锁查询没有ORDER BY。不能从“不同文件/组”推出可并行或无死锁。|
|产品保存|自有Serializable；按原资料/主数据/单位/客供资格校验次序。更新/删除先锁目标产品，新增可能先按SKU查询；这些早于共享事实。边界再读目标产品、无锁全图及补组件，最后写产品。|与BOM的先BOM后产品次序不同，但PG事实没有机械BOM更新锁；不能声称已有反向锁死锁，也不能保证全部等待无环。新增共享引用者、改角色与保存交错尚需实测。|
|真实需求建立|权限/输入→公共命令C租约→来源S/毛需求U发现、获取与复核（事务外）→Begin Serializable→LockAsync借用既有C→Find回执→依次锁Gross→活动Net及全部历史修订→根产品→普通共享组成→直接子产品→供应商→写Net→日志→回执→提交。|当前PG C借用不再次发锁SQL，Find通常是父事务首次数据快照，早于产品等待。根/子读取须按同快照处理；不是“等到产品锁后快照自动刷新”。S/U只覆盖来源与毛需求，不覆盖BomNo；不得用它证明所有BOM写者互斥。|

来源准备使用当前 `PostgreSqlGrossDemandDocumentRepository` 的真实发现/复核，整组释放重准备只发生在业务委托与首次快照前；不恢复旧M27 Reader。当前建立方法用 `AcquireBeforeTransactionAsync([], GrossIds)`，覆盖声明Gross及同来源成员；PG物料仓储消费同会话Gross租约来保护空活动范围和MAX修订→新增。命令准备先于S/U，借用租约不得释放别人所有权。源/物料后续更复杂命令的CommandItem准备不是本片新增BOM保护。

非BOM写者也已核对：产品普通保存实际可写BomNo/BomVersion、角色、软删以及比例校验依赖的资料，必须参与共同串行验收；默认供应商专用命令仍连接默认隔离，锁关系→产品→供应商，只写DefaultSupplierId/审计/Version；主型号同步写描述、配置、默认供应商、重量/体积或主型号编码及Version，不写BOM号/有效标记/物料角色，但会影响需求快照或等待后的父Version。不能为了BOM给这些路径无条件增加全图业务锁。全仓其他领域启停/更名/通用实体写者未逐方法审计，不宣称所有Product写者已经封闭。

临时兼容/重复读取：版本数值历史查询仍为只读核验；withUpdLock在PG事实里是兼容参数，不是假范围锁；全图读和定点父/子读分别提供结构与等待后资格，产品边界重复读也服务候选比较。先测一致性再判断冗余，不能删除查询或改变校验次序来追求简化。若出现真实反向等待或过期资格，下一轮仅调整PG必要锁SQL/诊断和对应测试；公共执行器、Core/接口、业务次序或语义改变必须由主控重新定界，不先建BomNo全局锁。

## 当前测试证据逐项区分

|证据|真实backend与入口|已写入测试的证明|不能替代的证明|
|---|---|---|---|
|PostgreSqlBomQueryTests.cs|记录连接+真实Dapper，PG仓储；不执行数据库SQL|无效历史输入免SQL、DTO/实体映射、参数、存在口径、页参数/搜索及父事务转交|ICU实际行为、保存服务、并发与真实回滚|
|BomSharedQueryTests / BomReferenceQueryTests / BomCollectionQueryTests|两提供者实际仓储SQL配记录连接|三类型空映射/输入组、锁提示差异、当前引用、历史字典和单次枚举|模拟返回表并非真实数据库分组或SSI证明|
|BomSaveEntryTests / BomSaveRollbackTests|真实BomService与共享校验器；DispatchProxy/内存集合/事务替身|单项收口、wrapperVersion、共享父、异常标记、最终图、整批回滚、提交未知和清理故障、模拟序列化分类|真实PG更新列/实体持久结果、40001、连接竞争|
|ProductBomSaveBoundaryTests|真实产品服务，局部仓储/内存事务|新空编号/合法绑定、已绑定禁止换组/停用、父子删除/软删绕过、角色候选图、版本/回滚|真实产品与BOM跨连接并发、全部非BOM写者|
|GrossDemandDecompositionPlannerTests|真实纯Planner，无DB|共享不同正标记、真实根自环、来源不回写、直接客供数量/单位、非标/生产约束|仓储查询、事务或新旧快照|
|SharedBomMaterialDemandEstablishmentTests|真实建立服务+管道/执行器/Planner；全部仓储和会话为替身|4个展开数据行：正标记1/3两成功、根自环及非法客供终端两阻断；唯一Serializable、同事务调用和物理来源不变|真实PG C/S/U准备、回执/日志/Net提交、保存后读取及等待取消|
|PostgreSqlBomQueryTests.Integration.cs|真实PG查询仓储，元数据插夹具，ReadCommitted父事务，回滚|10个展开场景：当前/历史/存在、软删、搜索、引用、历史集合、来源失效3种、共享事实含空输入/空组；未调用真实保存或需求建立|Serializable空范围并发、真实保存后消费；没有独立连接回滚观察及预登记UID清单，不能借文件注释声称已增强|
|PostgreSqlBomConcurrencyTests.Integration.cs|真实PG仓储+BOM/产品Application服务；透明事实读取屏障|4场景：组成合并环、产品绑定合并环、删除对新增引用、Serializable仓储读旧快照/新事务见更新；真实更新Version+1|角色竞争、同组空范围追加、完整需求建立。旧快照读者直接调用仓储且GetById无更新锁，不等于真实BuildPlan等待次序。|

历史证据只引用MAIN《后端整体架构与文件夹职责建议》第二十一批：14项PG（10查询+4双连接）通过、50表17,595行摘要相同；相关离线412通过/1既有失败及HTTP探针23通过。此前324项及44项亦是各自批次记录。本轮没有重跑，也没有当前构建日志/当前48文件SHA对应的实库结果，不能标“本基线通过”。分派告知故障库现含17,609行，与历史17,595不同；本轮未查库，不把历史净零当现在事实。

已有并发夹具先提交随机产品/组成，使用独立连接，结束按审计+编号/UID限定删除可变测试行后计数；不是外层回滚所有测试。查询夹具则只父事务回滚。两种策略必须分开，不把任何已有“测试库/备份”理解为重置许可。

## 最少缺失验证与下一任务

建议下一任务 `M03-BOM-CURRENT-VERIFY01`：仅新增测试文件及本轮新结果，保留已有断言；先离线编译/定向测试，Integration仅编译交主控串行运行。先做V1–V4，不重做已实现仓储、不扩递归Planner；V5用于确定真实失败/取消边界。各场景允许复用一个局部真实PG组合根。

|编号|最小场景|必需断言与预期|
|---|---|---|
|V1|同组现有空组成，两连接首次添加不同组件；另一个等值编号变体参数场景|使用真实BomService及PG，不插共享行锁占位；观察首次快照/等待，最终无重复行号、半套组成或静默丢请求；失败按实际类别核实，无业务委托重试。既有“[]返回空组”不能替代此场景。|
|V2|无BOM产品绑定已有组与另一连接改组成；角色变更与新增引用各一场景|全部共享父候选图/真实组件资格不能双成功留下环或非法终端；屏障只暂停真实读/等待且不人为构造死锁。两种先后均记录；不改校验次序。已有删除竞争可复用不重复补。|
|V3|真实BomService成功更新比例/组件后，新连接真实EstablishAsync；两个共享根正标记不同、来源父失效|实际PG根、BOM、子产品、Gross/Net、supplier、日志、回执；真实管道/执行器/Planner。输出根+直接客供件数量、BomLineId、Version、来源basis和物理来源不变；查询结果不可用旧版本/旧Reader替代。|
|V4|真实需求命令首次快照后，另一连接保存提交；在根读取前/组成后子产品等待点各布置一个可行交错|需求要么完整旧事实，要么明确拒绝，不能旧比例+新角色拼接；提交后的下一新命令/新Gross看完整新事实。预取更新锁若阻止writer先提交应验证实际等待，不能用屏障强行互等；旧快照仓储测试不是此证明。|
|V5|真实PG stale wrapperVersion；第一Gross已经写Net后第二Gross业务失败；取消在事务前租约等待及父事务写后提交前|版本冲突不变为序列化失败；独立observer确认整批Net/日志/回执无成功残留，父事务/租约正确释放且后继可用。不删已提交不可变事实。BOM保存无CT合同，不能伪造其等待可取消承诺。|

必要新测试白名单建议（须主控正式分派后才有写权）：

- `SwiftExport.Application.Tests/Database/PostgreSqlSharedBomCurrentProtocolTests.cs`：局部组合/观察记录的离线合同，仅补当前缺项，不复制已有几十项业务测试。
- `SwiftExport.Application.Tests/Database/PostgreSqlSharedBomCurrentProtocolTests.Integration.cs`：V1/V2，真实两个服务、两个连接、可控读/等待点和独立observer。
- `SwiftExport.Application.Tests/Database/PostgreSqlSharedBomMaterialDemandTests.Integration.cs`：V3–V5真实建立服务组合；透明屏障允许转发，禁止模拟核心PG读取和写入。
- 新任务专属结果及本人状态。现有源码/公共执行器/参数/元数据/测试环境/旧测试/项目/DI不默认可写。

前置及归属：主控冻结当前执行器、DbSession、PG命令/来源准备、Gross/MaterialDocument及BOM/Product依赖，确认其同scope组合及共享保护；测试执行者只读消费。若依赖变化先停写由主控同步，新测试不得自行接管公共。登录/权限在服务测试可用局部已授权身份与评估端口，但须声明并非真实HTTP/RBAC闭环；持久化仓储必须真实PG。已有完整仓储足够进入该验证，不以某个旧Reader未迁为阻塞。

取消限制是当前事实：BOM API/仓储方法没有CT；需求公共入口/准备/回执有CT检查，根/子/供应商和组成方法及部分Net方法没有CT参数，查询等待本身未全链传递。可验证取消最终不提交及租约等待释放，不能保证这些SQL等待立即中断。新增token合同或错误政策属于另任务，不在测试里放宽断言来伪装完成。

## 主控实库计划与数据保全

仅提出计划，本轮无连接/读取/写入授权的使用。沿用现有严格门禁目标 `192.168.3.12:5432 / dv_next_pg_failure_tests_20261004 / public`；主控先确认访问范围、用户授权、数据基线和故障库当前17,609行事实。不得改连原dv_next、备份或另建/重置库，也不因同机还有command测试库就自行切换。

主控串行跑，开跑前记录50表摘要与每个场景的随机前缀、Audit、预分配Product/Bom/Gross/Net/CommandId/日志UID及预期写表。仓储Add会生成最终BOM UID，除预登记请求ClientRowId，还需及时登记实际返回Uid；不能把提交后才生成的日志UID谎称已预登记，可通过透明真实仓储转发在Insert前记录。场景只有本例可变记录能初始化/清理，失败时保留完整身份清单。

V1/V2及旧仓储快照场景沿用提交少量可变product/bom夹具策略，结束按本例实际UID+Audit+原随机编号限定清理，组成先于产品；独立观察连接核验残留0并比较非本例摘要，不执行按Audit无身份兜底删除。父回滚场景即使断言失败也finally释放并由独立observer核验，不把Rollback返回当唯一证明。

V3真实Establish成功会提交net_demand、business_operation_log、command_receipt；日志/回执只追加，禁止删除、UPDATE、禁触发器或reset。主控须在执行前批准**留存计划**：同例Product/Bom/Gross/Net及日志/回执一并保留，逐UID列出预期增量（无默认供应商时不种supplier），外部基线不变；不能清可变父事实留下悬空审计解释。未批准留存，先仅编译该成功场景，不执行或改成假提交。V4成功留存同样处理；V5事务失败净增0，observer按全UID清单检查。最终50表净变化应等于批准留存清单而非统一要求0；序列变化另记，不当业务行残留。

原业务偏差只登记：树显示遇循环返回部分、validate独立SKU递归口径、BOM实体旧“指定版本/父归属”注释、修改记录/CommandId目标未实现、PG/SQL排序权重差异以及其它写者清理协议；均不借本轮调整业务行为。BOM地图有早期“共享写入尚未实现”与后文已实现并存，工程总纲“PG仅为计划”也过期；依现源码及最新分派判断，不修改公共文档。

## 读取范围、哈希与未验证项

主要完整读取：两BOM仓储、IBom、Bom服务及兼容分部、共享校验器、产品保存两分部、真实需求建立服务/Planner、PG通用基类及SQL写段、事实读取器、PG查询/并发测试、共享需求离线测试。额外按方法/章节读取Controller/BaseService/DI、产品同步/供应商写者、当前命令/来源/物料准备与会话、查询合同测试及地图/总纲。检索限应用、Core、Infrastructure、WebApi和相关测试中上述符号与产品目录写者，未读弃用资料，未全仓审计每个实体写入口。缺失候选文件名和Windows通配路径检索已改为实际目录/文件定位，不把失败检索作为无调用证据。

下表记录关键MAIN文件：A为主体读取阶段采样22项，B为补充调用链读取阶段采样26项；结束重新采样全部48项。不是Git HEAD证明，也不是文件整轮未曾被改后恢复的证明；只能证明两个采样时点的字节一致性。未检查Git状态/未提交diff；未读取凭据值，报告无连接密钥；本报告UTF-8。所有构建/测试/数据库结果均未运行，符合只读分派；没有当前HTTP、SQL Server实库、全量/WPF、并发性能或50表实测净变化结论。

|阶段|MAIN相对路径|读取阶段SHA256|结束SHA256|
|---|---|---|---|
|A|SwiftExport.Core/Interfaces/IBomRepository.cs|664268A3BCF231D7F5725055619AD9B2B8A4BA3CF39BC0014FA841E3612961C8|664268A3BCF231D7F5725055619AD9B2B8A4BA3CF39BC0014FA841E3612961C8|
|A|SwiftExport.Core/Entities/MasterData/Bom.cs|E4D3E8FD873596F7224200689F0594C9EDAD5F7768BD5205AC173E09C7844FCB|E4D3E8FD873596F7224200689F0594C9EDAD5F7768BD5205AC173E09C7844FCB|
|A|SwiftExport.Core/Entities/MasterData/Product.cs|12A845EFC6E2DD6F3BFFB85DAF84957ED5646824CA2F2A323C51F7A36383525E|12A845EFC6E2DD6F3BFFB85DAF84957ED5646824CA2F2A323C51F7A36383525E|
|A|SwiftExport.Infrastructure/Repositories/Products/BomRepository.cs|C7210FF31CCCB21976E408DCAFFDBD6C5EE02DB441A7CB4ED8CEE0C88CFD9B24|C7210FF31CCCB21976E408DCAFFDBD6C5EE02DB441A7CB4ED8CEE0C88CFD9B24|
|A|SwiftExport.Infrastructure/Repositories/Products/PostgreSqlBomRepository.cs|187FF1C3CA2AE88A21CC9C90963DF5545D69F47CAC7F9397EE9D7CE25E66082F|187FF1C3CA2AE88A21CC9C90963DF5545D69F47CAC7F9397EE9D7CE25E66082F|
|A|SwiftExport.Infrastructure/Repositories/Products/BomSharedSaveFactsReader.cs|1368E92FC6FB78248CCED0AF6884DBCF7CFE2C16867E92B2FEC92A5D02F7627E|1368E92FC6FB78248CCED0AF6884DBCF7CFE2C16867E92B2FEC92A5D02F7627E|
|A|SwiftExport.Infrastructure/Repositories/Base/PostgreSqlDapperRepository.cs|FBF956CAF619F74D106EDC85F3131B162A81E855A5AC0667490F3596F1889CFC|FBF956CAF619F74D106EDC85F3131B162A81E855A5AC0667490F3596F1889CFC|
|A|SwiftExport.Application/Services/Products/BomService.cs|795CABF3C73EC0B0BA588B6B12F1408BC4496F124614C07B876E9D3E1F75B6BB|795CABF3C73EC0B0BA588B6B12F1408BC4496F124614C07B876E9D3E1F75B6BB|
|A|SwiftExport.Application/Services/Products/BomService.兼容写入.cs|FDF312649B2759D77994C9816256BD65FCF3AE1F0F18428595CE33860A4BC3F3|FDF312649B2759D77994C9816256BD65FCF3AE1F0F18428595CE33860A4BC3F3|
|A|SwiftExport.Application/Services/Products/SharedBomStructureValidator.cs|FCB3C8C11760E74FF9C56DDC3C906C093FF0B2B3CB34F4475B6C1AFAD9ECA149|FCB3C8C11760E74FF9C56DDC3C906C093FF0B2B3CB34F4475B6C1AFAD9ECA149|
|A|SwiftExport.Application/Services/Products/Catalog/Maintenance/ProductMaintenanceService.cs|958C5C68FBEB7AC3DB9AC7D7779D563C1B82006F76CB2D9C014C28698884507E|958C5C68FBEB7AC3DB9AC7D7779D563C1B82006F76CB2D9C014C28698884507E|
|A|SwiftExport.Application/Services/Products/Catalog/Maintenance/ProductMaintenanceService.SaveBoundary.cs|DD330D17BC133BA59D4929E78F19D44F5600FBB3A891066BC97D3AD10099D8A6|DD330D17BC133BA59D4929E78F19D44F5600FBB3A891066BC97D3AD10099D8A6|
|A|SwiftExport.Application/Services/DemandPlanning/Materials/Establishment/MaterialDemandEstablishmentService.cs|ECE8BB0A4C682690CBFD2C5F5F0E6B34425317C825A61A9D6DF890473C0F3140|ECE8BB0A4C682690CBFD2C5F5F0E6B34425317C825A61A9D6DF890473C0F3140|
|A|SwiftExport.Application/Services/DemandPlanning/Materials/Establishment/GrossDemandDecompositionPlanner.cs|DFAD55F0B22C09402DE6CC4AD67FBCEAE5DFBE2457260862C5AA488FEBF7987D|DFAD55F0B22C09402DE6CC4AD67FBCEAE5DFBE2457260862C5AA488FEBF7987D|
|A|SwiftExport.Infrastructure/DependencyInjection.cs|BF279B5AD5CA6F06F8AD366EAC2B9873AE7D241CE2F7E851D3426BF9FAEA2844|BF279B5AD5CA6F06F8AD366EAC2B9873AE7D241CE2F7E851D3426BF9FAEA2844|
|A|SwiftExport.Application.Tests/Database/PostgreSqlBomQueryTests.cs|A68481AA87B243F0FF0F918B2C742BCA5B4D2212C9D4AB7695B767480F24BCE1|A68481AA87B243F0FF0F918B2C742BCA5B4D2212C9D4AB7695B767480F24BCE1|
|A|SwiftExport.Application.Tests/Database/PostgreSqlBomQueryTests.Integration.cs|F5030755EE09E30E24B6F985452774C54180E46A1A188729B74CCC3FB7E22214|F5030755EE09E30E24B6F985452774C54180E46A1A188729B74CCC3FB7E22214|
|A|SwiftExport.Application.Tests/Database/PostgreSqlBomConcurrencyTests.Integration.cs|82F9CD25FF607F8FC6868238C2885446C4A103AF12233DAFFCBBAB2345F498B2|82F9CD25FF607F8FC6868238C2885446C4A103AF12233DAFFCBBAB2345F498B2|
|A|SwiftExport.Application.Tests/DemandPlanning/SharedBomMaterialDemandEstablishmentTests.cs|411B06DD8FEDA295BA5F454702C313405E1F7A24479EBE2CFBC786833B0EDEA9|411B06DD8FEDA295BA5F454702C313405E1F7A24479EBE2CFBC786833B0EDEA9|
|A|SwiftExport.Application.Tests/Products/BomSaveEntryTests.cs|31BD087309BFCF5C449BD7D516DBB82B48E7ED35D316229F49FB394B4B1CF4AA|31BD087309BFCF5C449BD7D516DBB82B48E7ED35D316229F49FB394B4B1CF4AA|
|A|SwiftExport.Application.Tests/Products/BomSaveRollbackTests.cs|65A2E89CCC061D2727321C7CCF86B4512D82881371528E68F10D70A88B9EDFF5|65A2E89CCC061D2727321C7CCF86B4512D82881371528E68F10D70A88B9EDFF5|
|A|SwiftExport.Application.Tests/MasterData/ProductBomSaveBoundaryTests.cs|556E18ABB71821F40A507B080F69F5D302A2B9E0F6C3FF5357C9A6F815FFD0A5|556E18ABB71821F40A507B080F69F5D302A2B9E0F6C3FF5357C9A6F815FFD0A5|
|B|SwiftExport.Application/Commands/BusinessCommandExecutor.cs|2688DD4F5B0DFA19543103FCE2890CD1D383A248DB08A0B2B946DFD2DC749295|2688DD4F5B0DFA19543103FCE2890CD1D383A248DB08A0B2B946DFD2DC749295|
|B|SwiftExport.Application/DependencyInjection.cs|949E6F740ADA32AFD0B0DC5EB403CADE01F33E8CD8B5CBA92D2E22104D8AC8F5|949E6F740ADA32AFD0B0DC5EB403CADE01F33E8CD8B5CBA92D2E22104D8AC8F5|
|B|SwiftExport.Application/Interfaces/IBomService.cs|315C18BFB629CBA0653072FB58267B1812D4B584FFB767AD797868556CA2F607|315C18BFB629CBA0653072FB58267B1812D4B584FFB767AD797868556CA2F607|
|B|SwiftExport.Application/Services/Base/BaseService.cs|FC5D085C48A09F0298208FEB62F7A6604DFFC93726548EA8F4C3AE7F3071739A|FC5D085C48A09F0298208FEB62F7A6604DFFC93726548EA8F4C3AE7F3071739A|
|B|SwiftExport.Application/Services/Products/Catalog/DefaultSupplier/ProductDefaultSupplierService.cs|3F80824525A0B5CB87E0869AC286D1C79E765B5E8996270405AB824B5ADF29DB|3F80824525A0B5CB87E0869AC286D1C79E765B5E8996270405AB824B5ADF29DB|
|B|SwiftExport.Infrastructure/Repositories/Base/DapperRepository.cs|4DC73D3E698C3ED6C62EF75186DA1D13C8FBBA13DD04503581410C09BB5F0435|4DC73D3E698C3ED6C62EF75186DA1D13C8FBBA13DD04503581410C09BB5F0435|
|B|SwiftExport.Infrastructure/Repositories/Products/Catalog/PostgreSqlProductRepository.cs|816BDA9AEF9C7E0496D125912BBE470DFB64FF846F3C83DCDBBF1BEF201DA352|816BDA9AEF9C7E0496D125912BBE470DFB64FF846F3C83DCDBBF1BEF201DA352|
|B|SwiftExport.Infrastructure/Repositories/Products/Catalog/PostgreSqlProductRepository.Query.cs|2D9322004F101F2856E0AB92DBAD3A56D0DEE5A780E4730AC62972CE0E636DA1|2D9322004F101F2856E0AB92DBAD3A56D0DEE5A780E4730AC62972CE0E636DA1|
|B|SwiftExport.Infrastructure/Repositories/Products/Catalog/PostgreSqlProductRepository.MasterSynchronization.cs|ABAF5A356713A16E8B3B7555FC69252996E265CC0CC42E019620B43489DDE5CB|ABAF5A356713A16E8B3B7555FC69252996E265CC0CC42E019620B43489DDE5CB|
|B|SwiftExport.Infrastructure/Repositories/Products/Catalog/PostgreSqlProductRepository.Supplier.cs|DF80FE02370E8E787BBA3900C00AB3EB09D6379E57D7CA17C0677246DC275B3C|DF80FE02370E8E787BBA3900C00AB3EB09D6379E57D7CA17C0677246DC275B3C|
|B|SwiftExport.Infrastructure/Repositories/Commands/PostgreSqlCommandReceiptRepository.cs|2B4DADA85C83E50941364F11E6932875F8CB073BF01D7B35E05DAC996F740BF2|2B4DADA85C83E50941364F11E6932875F8CB073BF01D7B35E05DAC996F740BF2|
|B|SwiftExport.Infrastructure/Repositories/DemandPlanning/Sources/Document/PostgreSqlGrossDemandDocumentRepository.cs|EA2A915D96CB9D8F361AFDDFC105A0B23BAE46D6A57B4BF62D0221E9441AC61F|EA2A915D96CB9D8F361AFDDFC105A0B23BAE46D6A57B4BF62D0221E9441AC61F|
|B|SwiftExport.Infrastructure/Repositories/DemandPlanning/Sources/Document/PostgreSqlGrossDemandDocumentRepository.MaterialPreparation.cs|B64A330D040B4342631AD098B87E01ECA7C49698C9025A7C0C56E4647825EF44|B64A330D040B4342631AD098B87E01ECA7C49698C9025A7C0C56E4647825EF44|
|B|SwiftExport.Infrastructure/Repositories/DemandPlanning/Materials/Document/PostgreSqlMaterialDemandDocumentRepository.cs|341B5E7C516B7E578669B9C873A72D9A1298258C7158EEF5E3BA2EC827811B1B|341B5E7C516B7E578669B9C873A72D9A1298258C7158EEF5E3BA2EC827811B1B|
|B|SwiftExport.Infrastructure/Data/DbSession.cs|AAEED4B671826D92EE92BA97727B3887F8399C1FD87F3F86D187AE11C20EA077|AAEED4B671826D92EE92BA97727B3887F8399C1FD87F3F86D187AE11C20EA077|
|B|SwiftExport.Infrastructure/Data/PostgreSqlCommandLock.cs|C714CFBF51763F244752F0EAD61536D8567194334E4A0B271095B3432495FCCE|C714CFBF51763F244752F0EAD61536D8567194334E4A0B271095B3432495FCCE|
|B|SwiftExport.Infrastructure/Services/Auth/AuditStampService.cs|7E82ACA804F21F7E8308D0D2F4040BBEB47BE71B7294371A39128DCA75688525|7E82ACA804F21F7E8308D0D2F4040BBEB47BE71B7294371A39128DCA75688525|
|B|SwiftExport.Application.Tests/Database/PostgreSqlIntegrationEnvironment.cs|33594E10D1F07C029848A6B720D23D82DAF42C9438119B905B89B85418DDD6DE|33594E10D1F07C029848A6B720D23D82DAF42C9438119B905B89B85418DDD6DE|
|B|SwiftExport.Application.Tests/Database/BomSharedQueryTests.cs|49B1168D63989A69491B8B64CBB5C62FC8A3D25AB8BD3D3DDA6B82E6C91FF787|49B1168D63989A69491B8B64CBB5C62FC8A3D25AB8BD3D3DDA6B82E6C91FF787|
|B|SwiftExport.Application.Tests/Database/BomCollectionQueryTests.cs|7C8201587DAA3A24C070F37EFA3CD00FA9426F5B2BBFEE3C6ABB7D59798DD1A3|7C8201587DAA3A24C070F37EFA3CD00FA9426F5B2BBFEE3C6ABB7D59798DD1A3|
|B|SwiftExport.Application.Tests/Database/BomReferenceQueryTests.cs|581FD58C490916C01A7AAAC9AA61AD1EA817B9A19003C6F74C08BD2AA516AE7C|581FD58C490916C01A7AAAC9AA61AD1EA817B9A19003C6F74C08BD2AA516AE7C|
|B|SwiftExport.Application.Tests/DemandPlanning/GrossDemandDecompositionPlannerTests.cs|58D59F853959FC0C51DF1D67CC29ADA3FDFFBFCF82FD5F946B82A7005AC4A8DD|58D59F853959FC0C51DF1D67CC29ADA3FDFFBFCF82FD5F946B82A7005AC4A8DD|
|B|SwiftExport.WebApi/Controllers/Products/BomController.cs|B037D62C95338FD6EE56B9775F0E823D75F09939BE0FCC3092201838A12D4172|B037D62C95338FD6EE56B9775F0E823D75F09939BE0FCC3092201838A12D4172|
|B|SwiftExport.WebApi/Controllers/Base/BaseController.cs|0A53778AA40083429CC8C2AEE347D5278A54227B3EE08E32AF67582C8F446BDC|0A53778AA40083429CC8C2AEE347D5278A54227B3EE08E32AF67582C8F446BDC|
|B|SwiftExport.WebApi/Controllers/DemandPlanning/Materials/Establishment/MaterialDemandEstablishmentController.cs|78666B858405782FAB7E990CCA0B1AE50045E6387E2E961109C7C3CD16167725|78666B858405782FAB7E990CCA0B1AE50045E6387E2E961109C7C3CD16167725|
|B|docs/后端整体架构与文件夹职责建议.md|B4C44F280E7D7D714B1FE679F749337FD8F57D47DC1A50D4DF4223137FCE4188|B4C44F280E7D7D714B1FE679F749337FD8F57D47DC1A50D4DF4223137FCE4188|


