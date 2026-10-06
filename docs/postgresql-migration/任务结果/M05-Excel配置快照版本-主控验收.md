# M05 Excel配置快照/版本主控验收

2026-10-06 04:44，本批1真实PG首次通过、0失败/0跳过、exit0，方法582.8509毫秒（摘要601毫秒）。现RuntimeWiring一个测试方法及透明主记录读取屏障共166行；原全文恢复SHA与上一批逐字节一致，不改服务/仓储/业务/DI/契约/API/配置/隔离/结构，不拆文件。必要编译0错误7既有警告4.16秒；不全量或重复绿色。

完整宿主AddWebApiServices、现dv只读身份审计、PG写6580/读9048/观察1436：
- 原Create V1，同组同名原约束返回EXCEL_READ_DUPLICATE_NAME且无重复头/事务残留。
- 原完整字段保存V2、旧SKU/Quantity配置持久化。读请求实际读取旧头后仅暂停测试时序，原事务IsolationLevel与服务器SHOW均证明RepeatableRead。
- 写请求V2→V3成功提交（主信息与SKU变更、Quantity原软删、新Weight），释放旧读取后全部DTO JSON等于此前持久化旧DTO；随后新Get完整看到V3。
- 同SKU字段UID保持、缺席Quantity正确软删、旧ExpectedVersion2更新被VERSION_CONFLICT拒绝，完整头和全部字段JSON不变。
- 第三连接只锁本批新头，原两个ExpectedVersion3保存都形成真实等待；服务器图1436→6580→9048（箭头先持有/排队→等待），不是仅依赖延迟或未完成Task。
- 释放后唯一赢家RaceA形成V4、另一VERSION_CONFLICT；最终主信息与完整字段属于赢家，Session事务/租约清理。

本批02e119c9-bec4-4b8f-a9c3-9dd718b5e184仅192.168.3.12:5432 / dv_next_pg_failure_tests_20261004 / public新增保留4资料：
| 表 | UID | 最终状态 |
|---|---|---|
| excel_read_profile | 5c5fae49-ba80-4da2-926a-4a3504701ee9 | PGM0502E119 / RaceA、V4、D末行列、dv创建 |
| excel_read_field | bcd8203b-1d8d-4fa8-bfe2-4a3504701f31 | Line.Sku、RaceA/D8、V3、活动、dv创建 |
| excel_read_field | 0bb761ee-0cf9-418d-9929-4a3504701f33 | Line.Quantity、NULL表名/D8、V2、已软删、dv创建 |
| excel_read_field | f1553e06-1474-4a28-aa86-4a3504701f65 | Line.Weight、NULL表名/F8、V2、活动、dv创建 |

50表52,605→52,609行，精确排除上述4新UID后全部原数量/完整行MD5相同；库存序列50/true不变，三backend/其他活动/advisory/testhost0。没有旧资料修改/删除、nextval、DDL、清库、复位或运行/RBAC/备份库写入。SQL头/字段读写全部由原仓储完成；屏障无替身数据/额外锁/隔离改动。身份注入只供本测试原服务审计，不当dv正常密码登录通过。

[执行登记](M05-Excel配置快照版本-执行登记.md)、[实际命令](M05-Excel配置快照版本-实际命令.ps1)、[验证证据](M05-Excel配置快照版本-验证证据.json)。TRX为TestResults/postgresql-migration/m05-excel-profile-snapshot-version-native.trx，SHA256 C762CD29C6FD59179F7BEB86D465480B5873732695AFFB9131CB039ED64AA4A6；当前测试源0FC3569708EF342C6DE039E813030EB6ED1F2720D03E7963D5725637A37DE394，原全文恢复BBBA76A22BEB90AAC27E98398A055CF34A789248B608A63AE34AD9871B34DA01。证据含实际等待图、4UID/来源归属、相关源码SHA及前后全摘要。取证调度首次JS声明语法错误导致未启动任何工具，仅修取证表达式，原生测试未重跑。

M05已列必要字典事务/父子归属/版本整批回滚、可空汇率、Excel RR完整快照/主版本竞争证据补齐；原两配置十字段HTTP读取不重跑。标M05必要范围验证通过，不外推字段后段故障/取消/全部CRUD、正常密码登录或P08整体。B03切换缺口继续暂缓；正常dv登录/G6、P08最终/P09仍开放。A/B未续派、未提交/推送/合并或部署，本批STOPPED、目标active。

