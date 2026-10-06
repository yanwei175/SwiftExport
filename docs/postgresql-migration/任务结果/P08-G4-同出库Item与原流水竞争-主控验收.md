# P08-G4 同出库Item与原流水竞争主控验收

2026-10-06 02:18 +08:00，本批首次真实PG验证通过（现有合法消费者限定）。只既有RuntimeWiring新增一个方法及结果记录，没有生产代码、业务规则、API、DTO、DI、锁、隔离、结构或仓储拆分修改。成功新事实全部保留，未普通/全量或重跑已有绿色。

## 真实入口与防线

按原Supplier.Add→供应商虚拟仓Create→独立采购Create/Confirm→入库Create/Post产生本批新库存2；一供应商一活动仓规则完整执行，未复用旧活动仓资格。原出库Create生成唯一新行与CommandItemId，外部Post/Reverse命令只传CommandId/ExpectedVersion及原反向原因，不接受修改内部Item或原流水身份。

两个不同Post命令6/7处理同一人工出库ExpectedVersion1。第三连接PID4400只持新仓FOR UPDATE，第一原Post PID3940在仓锁等待，第二PID6128在第一持有的原单据头等待，真实服务器图3940→4400、6128→3940；双方具有业务事务及原出库RowShare关联。释放后第一成功/单据V2/余额0V3，第二按原版本返回VERSION_CONFLICT；内部Item55b10c45-1a10-4889-a080-4a3503e9850a只生成一条正式流水。

两个不同Reverse命令8/9随后处理同一已过账出库ExpectedVersion2。第三连接PID3940只锁本批原库存流水14504ccb-460c-4546-8072-4a3503e9864e，第一原Reverse PID4400实际等待库存内核原流水锁，第二PID6128等待第一持有的原单据头；真实服务器图4400→3940、6128→4400。释放后第一成功/单据V3/反向状态8，第二VERSION_CONFLICT；原流水完整JSON前后相同，只生成一条对应反向，余额恢复2V4。

最后原服务反向本批入库命令10，余额0V5/LastLedgerSequence50；四流水47—50两对原/反向事实留存。失败命令7/9日志与成功回执均0。全部请求Task.WhenAll终态，Session.Transaction/CommandLease/SourceDemandLease清理断言通过；最终三个相关PID目录/advisory0。观察超时不能代替终态，当前没有遗留进程或业务事务。

## 范围判断

本批覆盖实际合法单据消费者重复提交时对同内部Item和同原流水的保护。拒绝由原单据头和版本边界先发生，不声称两个调用都到达同一库存Item锁/原流水锁。不能修改旧行Item或扩DTO强造不同业务单据合法共用内部Item。

G4其余已列防线继续按实际范围复用：[不同业务首次空余额](P08-G4-跨业务首次库存-主控验收.md)、[负库存及原完整反向](P08-G4-负库存竞争-主控验收.md)、[全局Item/原反向/原替代三唯一约束及不可软删](P08-G4-库存唯一约束-主控验收.md)。源审查要求选择现有合法入口、无合法消费者不为测试新增功能；CorrectAsync只有内核声明/实现、无Application/WebApi生产调用方，纠错业务入口不属本次迁移新增能力。原替代唯一数据库防线已实库23505准确约束名验证，不能说业务纠错已被运行验收。

据上述证据，G4列明的现有合法消费入口与数据库防线已补齐；P08整体及未来真实新增消费者仍独立验收。不扩展全部业务/隔离/网络或纠错组合矩阵，不提升为任意并发无死锁证明。

## 命令与证据

- `dotnet build SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore -v minimal`：exit0、0错误/7既有MSTEST警告，6.72秒。
- `dotnet test SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~RealPg_RuntimeSameOutboundItemAndOriginalReversalCompetition' --results-directory TestResults/postgresql-migration --logger 'trx;LogFileName=p08-g4-same-outbound-native.trx' -v minimal`：首次exit0/1通过/0失败/0跳过，1.2429653秒。
- [实际连接保护/门禁/环境恢复命令](P08-G4-同出库Item与原流水竞争-实际命令.ps1)、[当前TRX](../../../TestResults/postgresql-migration/p08-g4-same-outbound-native.trx)、[原生身份及服务器图](P08-G4-同出库Item与原流水竞争-原生事实.json)、[完整数据保护及最终9条SQL](P08-G4-同出库Item与原流水竞争-验证证据.json)。
- 当前RuntimeWiring文件SHA256：8FA07FBB2AC21395D12E0D324A750D316C5919AD8489FD535C16274A66E147A7。真实等待记录SQL前200字符、xact_start及blocking数组；不以固定睡眠猜测竞争或伪造PostgresException。
- 使用dv原活动管理员、原客户cc533ab1/产品00008dec，权限与资格仍原服务执行；没有重跑、改有效断言或从头重造失败批次。

## 数据保护和交接

仅192.168.3.12:5432/dv_next_pg_failure_tests_20261004，批次0a77ecca-8ebd-4da1-812f-a8f1c1f85f00。原50表52518行完整冻结；运行后50表52552行，34精确新增分13表（供应商1/仓1/采购头行各1/入出库头行各1/余额1/流水4/日志9/回执9/号段3）。排除这34个精确UID后，原50表52518行全部数量及完整MD5逐表同；没有旧数据更新、清库、软删清理、重置或覆盖导入。

新PO/IR/OI660210号段各1，库存序列46→50正常推进/本批无缺口；旧PO202610=4、CR650410=1未变。IR6602100001及OI6602100001均V3/反向状态8/未软删；成功命令0—6、8、10日志及回执各9闭合，失败7/9均0。没有dv_next/备份或DDL写入，无需结构同步。

新供应商88be94ed-8630-4293-9327-4a3503e98371，新仓ab00be69-2f47-4cb5-881b-4a3503e983c3，PO fbd8b5ec-8f8f-4d43-ab8b-4a3503e983f7，IB b58dd7bf-095a-40d0-bb87-4a3503e9848e，OI 4df39694-521c-49fa-89ec-4a3503e98506，余额6854a861-3728-4cb2-b6db-4a3503e984e1；完整UID见JSON。

主控本批STOPPED、目标active，所有构建/原生句柄terminal；A/B未续派，成果未提交/推送/合并。剩余G6运行验收、P08阶段/最终回归及P09源实现退出继续，迁移尚未整体完成。
