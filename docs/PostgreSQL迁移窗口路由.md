# PostgreSQL当前接线与迁移记录入口

核对日期：2026-10-06。本文件整篇替换旧迁移窗口分派及批次累计描述。现行业务说明依据源码；原实际迁移、TRX、数据库保护和用户授权记录保留在[postgresql-migration](postgresql-migration)，不以旧阶段文字重新启动开发。

## 当前源码事实

Infrastructure.DependencyInjection注册PostgreSqlConnectionFactory、PostgreSqlExceptionClassifier、PostgreSqlDapperRepository及PG专用仓储，IDbSession与DbSession共享scoped实例。后端目标和当前接线都是PostgreSQL+Dapper，不维护长期双库切换。BOM/产品、来源、采购、库存及Shipping的实际事务和准备协议以当前调用链为准。

本位币B03已按用户确认规则修复：所有RateToBase重置1:1并通过Message提醒重设。系统字典列表既有离线失败按用户要求保留；历史测试结果不重新记为当前全量通过。

## 协作与验证约束

原主控/执行A/B状态见迁移目录窗口状态；本文件不创建新窗口、worktree或委派任务。新开发必须有当前用户授权和明确文件归属，公共会话/事务/映射/DI/结构由主控串行处理；分模块不自动构成可并行资格。

读写授权须明确实例/库/数据范围，不输出凭据、不写备份、不用“测试库”泛化清库授权。当前已有结构和数据，不照旧空库脚本重复建表/导入。定向真实PG验证与离线测试分别记录，模拟数据库不证明实际锁和约束。保护未提交成果，不自动回退、提交、合并、推送或部署。

## 当前入口

- [PG运行DI](../SwiftExport.Infrastructure/DependencyInjection.cs)
- [数据库结构说明](../SwiftExport.Infrastructure/Database/README.md)
- [主迁移清单与限定状态](postgresql-migration/主迁移清单.md)
- [问题及保留失败](postgresql-migration/问题记录.md)
- [本位币修复及实际证据](postgresql-migration/任务结果/P11-B03-本位币切换-20261006.md)
- [当前业务说明](业务文档索引.md)

本轮只整理文档，不构建、运行宿主或访问数据库；不把旧窗口权限和分派重新当活动任务。
