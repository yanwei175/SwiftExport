# Guides 索引

Guide 目录用于维护通用开发规范、架构规范和写作规范。Guide 写稳定规则，Skill 写执行流程；不要把具体业务项目规则放进公共 Guide。

## Backend

| Guide | 说明 | 状态 |
|---|---|---|
| [application-service-guide.md](backend/application-service-guide.md) | Application Service 职责、事务边界和用例编排 | 已从 `_review` 提炼 |
| [dapper-repository-guide.md](backend/dapper-repository-guide.md) | Dapper Repository 职责、SQL 和仓储边界 | 已从 `_review` 提炼 |
| [webapi-controller-guide.md](backend/webapi-controller-guide.md) | Web API Controller 输入输出和分层调用规范 | 占位待完善 |
| [database-schema-guide.md](backend/database-schema-guide.md) | 数据库表、字段、类型和审计字段规范 | 已从 `_review` 提炼 |
| [unit-of-work-guide.md](backend/unit-of-work-guide.md) | Unit of Work 事务边界和连接生命周期规范 | 已从正式 Skill 与 `_review` 提炼 |

## Frontend WPF

| Guide | 说明 | 状态 |
|---|---|---|
| [wpf-communitytoolkit-mvvm-guide.md](frontend-wpf/wpf-communitytoolkit-mvvm-guide.md) | .NET 8+ WPF CommunityToolkit MVVM 规范 | 已从 `_review` 提炼 |
| [wpf-mvvmlight-guide.md](frontend-wpf/wpf-mvvmlight-guide.md) | .NET Framework WPF MVVMLight 兼容规范 | 已从 `_review` 提炼 |
| [wpf-datagrid-guide.md](frontend-wpf/wpf-datagrid-guide.md) | ERP DataGrid 行状态、批量保存、验证规范 | 已从 `_review` 提炼 |
| [wpf-custom-controls-guide.md](frontend-wpf/wpf-custom-controls-guide.md) | WPF 自定义控件边界和复用规范 | 占位待完善 |
| [wpf-theme-resource-guide.md](frontend-wpf/wpf-theme-resource-guide.md) | WPF 主题资源、样式和资源字典规范 | 占位待完善 |
| [wpf-dialog-navigation-guide.md](frontend-wpf/wpf-dialog-navigation-guide.md) | Dialog、Message、Navigation 服务边界 | 已从 `_review` 提炼 |

## Shared

| Guide | 说明 | 状态 |
|---|---|---|
| [dto-guide.md](shared/dto-guide.md) | DTO、映射和跨层传输规范 | 已从 `_review` 提炼 |
| [refit-api-guide.md](shared/refit-api-guide.md) | Refit API 契约变更规范 | 占位待完善 |
| [service-result-guide.md](shared/service-result-guide.md) | 服务返回结果模型规范 | 占位待完善 |

## Workflow

| Guide | 说明 | 状态 |
|---|---|---|
| [safe-refactoring-guide.md](workflow/safe-refactoring-guide.md) | 小步、安全、可回滚重构规范 | 已从模板文档提炼 |
| [project-onboarding-guide.md](workflow/project-onboarding-guide.md) | 新项目接手和现状扫描规范 | 已从模板文档提炼 |
| [codex-task-writing-guide.md](workflow/codex-task-writing-guide.md) | 给 Codex 写任务和约束的规范 | 占位待完善 |

## 维护规则

- Guide 负责通用规范，不写成具体项目的业务规则。
- 从模板文档提炼 Guide 前，需要移除项目专属命名和业务约束。
- 与 Skill 强相关的流程细节优先放在 Skill；Guide 保留稳定原则和检查清单。
- 资料不足时标记 `TODO：待人工确认`，不要为了完整而编造规则。
