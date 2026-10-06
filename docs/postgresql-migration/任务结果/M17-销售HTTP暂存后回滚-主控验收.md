# M17 销售HTTP暂存后回滚验收

2026-10-06 03:57，Backend / 后端API工作流，主控串行。只补当前主清单实际Save部分写入后的回滚门禁，不重复前批销售/需求绿色。

最终 **1通过/0失败/0跳过，方法5.1523493秒、exit0**。通过原 `/api/sales-orders/detail/save`、原Controller/权限/CommandPipeline/BusinessCommandExecutor和真实PG仓储执行；账户为真实dv，开发中间件开启，无伪造令牌或ICurrentUser替换。见[原生事实](M17-销售HTTP暂存后回滚-原生事实.json)、[实际命令](M17-销售HTTP暂存后回滚-实际命令.ps1)、[TRX](../../../TestResults/postgresql-migration/m17-sales-http-stage-rollback-native.trx)、[完整证据](M17-销售HTTP暂存后回滚-验证证据.json)。

原HTTP创建两个合法非标行，描述/单位/正数量按原校验。修改请求先写订单头V2，再真实Stage两行到6/7；测试透明代理完整转发仓储，只在暂存完成后在同原事务执行SELECT 1/0，SQLSTATE **22012**来自PG服务器。原服务及HTTP错误过滤器返回 **500/UNEXPECTED**，不改原错误出口。

独立PG观察连接确认失败后完整头/行JSON与更新前相同，旧V1/行号1、2/Qty1、2恢复，更新回执0，Session事务及CommandLease/SourceDemandLease清空。随后遵守原2秒防抖，以同更新CommandId和相同输入重新请求，正常200，头V2、行V2/连续1、2/Qty2、3，两成功回执actor均dv；SO690310仍只取一次号，GrossDemand0。生产层未降隔离、自动重试或增加锁。

实际SalesOrderDraftService没有业务审计Append消费者，本批原日志不增加；不能把日志0称为“已写审计后回滚”，也不借迁移补审计。来源确认/审计/需求/库存联合沿用G1及其它独立证据，本批未调用。

## 失败与接续

首次编译四CS1061/5.29秒，是新测试租约属性命名错误；仅改成真实CommandLease/SourceDemandLease。后续必要编译0错误/7既有警告，分别5.61、3.79、3.21秒。

1. 首原生0.8317801秒失败：创建已成功，测试没有字符串枚举转换器而读取documentStatus失败。原头/两行/取号及成功创建回执全部保留，单独[首失败TRX](../../../TestResults/postgresql-migration/m17-sales-http-stage-rollback-first-parser-failure.trx)。仅修新测试解析器并按精确原头78db0c78-2af7-4606-9713-4a3504451697/V1/原备注与命令守卫恢复，创建请求原样重放，不重建替代批。
2. 第二原生2.8507600秒失败：已实际暂存并发生22012，但新测试助手误要求所有响应200；现有ServiceResultHttpFilter/ServiceErrorResponse明定技术错误500。只修新错误请求状态断言，保留全部UNEXPECTED/真实回滚断言；[第二失败TRX](../../../TestResults/postgresql-migration/m17-sales-http-stage-rollback-second-status-failure.trx)独立保留。实际库核已回滚，后续再用原精确头/原输入接续。

两次失败均定位为新测试错误预期，没有为绿色修改生产业务/API/现有有效断言；未普通全量或重复其它绿色。最终通过不抹除两次失败，也不称首次原生通过。

## 数据与边界

只写[执行登记](M17-销售HTTP暂存后回滚-执行登记.md)限定的192.168.3.12:5432 / dv_next_pg_failure_tests_20261004 / public。原50表52,596行冻结，最终50表52,602行，仅新头1/行2/号段1/成功回执2共6精确UID保留。排除6UID后 **全部原50表数量及全字段MD5相同**；dv账户、既有客户、之前M11等成功事实不变。库存50/true，5760/8848/7296全部backend及advisory0、其它活动0/testhost0。没有清库/删旧数据、运行库/RBAC库/备份写入或DDL。

源码当前SHA256 **8E8A8FD5B34590F9F2317081194574DCD51890B16A4AA059A3A4F702B80F59E9**；去掉本批新方法和嵌套透明观察器后原全文SHA仍CD77483EE2B8D48C2B6ED164DAE774CB27DDA00ED1EC777E041C708663D2313B、字节完全相同。只一个既有RuntimeWiring文件/结果记录，没有新派生仓储文件或生产/DI/配置/结构变更。

本批不能证明正常dv密码登录、dv_next运行库写入、源52720上界或其它MAX/空范围竞争。M17限定实际Save回滚已补，最终P08及P09仍待；本位币缺口及特殊字符差异继续按已确认边界。A/B未续派，成果未提交/合并/推送/部署，整体目标active。
