# M05 Excel配置真实快照/版本执行登记

2026-10-06 04:41，Backend / 后端API工作流，主控串行。现RuntimeWiring新增一个实库方法和测试透明读取屏障，共166行；原全文恢复SHA BBBA76A22BEB90AAC27E98398A055CF34A789248B608A63AE34AD9871B34DA01逐字节一致，新SHA 0FC3569708EF342C6DE039E813030EB6ED1F2720D03E7963D5725637A37DE394。生产/业务/仓储/DI/API/契约/配置/隔离/结构不改，不拆文件。必要编译dotnet build SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj --no-restore -v minimal：0错误7既有警告4.16秒。

目标192.168.3.12:5432 / dv_next_pg_failure_tests_20261004 / public；固定批次02e119c9-bec4-4b8f-a9c3-9dd718b5e184，分组PGM0502E119（原组格式允许的独立测试标识），名称PGM05_02e119_Old/New/RaceA或B，备注固定batch。原组及批次主配置0。开始50表52,605行全摘要冻结，库存序列50/true，其他活动/advisory/testhost0；所有旧数据保护。只读dv真实身份供审计，不代表正常密码登录。

完整宿主AddWebApiServices、两个不同PG请求作用域、第三观察连接。原Create产生新头V1，同组同名第二次原唯一约束转EXCEL_READ_DUPLICATE_NAME不留行；原Update完整合法字段Line.Sku/Line.Quantity形成V2。读取真实旧头后暂停一次（目标UID/forUpdate=false），PG事务仍为原RepeatableRead；另一原Update V2→V3提交（改Sku地址/主信息、删除Quantity、新Weight），释放读取后全部DTO应与先前持久化旧DTO JSON完全相同，随后新Get看到完整V3。Sku字段身份不变、缺席Quantity原软删；旧ExpectedVersion2更新原VERSION_CONFLICT及完整头/全部字段JSON不变。

第三观察连接只SELECT FOR UPDATE本批新头；两个原Update用同主版本3在不同连接真实等待，必须从pg_stat_activity/pg_blocking_pids证明两个等待（含软队列阻塞）后才释放。只有一个成功形成V4、另一VERSION_CONFLICT，最终字段/主信息属于唯一赢家，不能混合/重复新增。finally释放屏障/观察锁、取消并观察在途任务，Session事务/租约清理。

预计净新增1头3字段（Quantity已软删，Sku与Weight活动）共4行；实际UID由原实体生成，结束按本批新头及字段归属登记并保留。写入仅这4自有新行，无旧行修改/物理删除/重置/DDL/nextval或dv_next/RBAC/备份写入。成功事实不清理/重跑，不把新Name/Group当产品功能。

固定脚本仅failure门禁及本批显式标识、Workers1、一个原生方法；凭据只进程内读取。结束实际TRX/服务器等待、4精确新UID外原50表全摘要一致、库存/活动/advisory/三PID/testhost核对；未执行不得标通过。A/B未续派、未提交/推送/合并或部署；B03暂缓，G6/P08最终/P09仍开放。


2026-10-06 04:45 终态STOPPED：首次1真PG通过582.8509ms/exit0，4真实新UID已登记；RR完整旧DTO与新V3及原版本双竞争实图1436→6580→9048通过。仅新1头3字段留存，4UID外原50表52,605行全数量MD5同、库存50/true与三PID/活动/advisory/testhost0。M05必要范围验证通过，无生产/隔离/结构/其它库变更；G6/P08最终/P09待，目标active，A/B未续派/未提交。
