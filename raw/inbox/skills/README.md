# Skills 索引

Skill 负责任务执行流程，供 Codex、Claude 等智能体按场景调用。稳定规范放在 `guides`，Skill 只引用 Guide 并说明执行步骤、检查清单、禁止事项、验证方式。

| Skill | 适用场景 | 必读 Guide / Skill | 状态 |
|---|---|---|---|
| `backend/dotnet-db-uow` | .NET 数据库访问基础设施、连接工厂、UnitOfWork、DI 注册 | `guides/backend/unit-of-work-guide.md` | 既有可用 |
| `backend/dotnet-webapi-template` | 从 `templates/dotnet-webapi` 初始化、同步或审查 WebAPI + Dapper 后端模板 | `templates/dotnet-webapi/README.md`、`templates/dotnet-webapi/docs/backend-architecture.md`、`guides/backend/application-service-guide.md` | 新增 |
| `backend/backend-contexts` | WebAPI 默认 DbSession、ServiceBaseContext、RepositoryBaseContext、scoped 生命周期和事务边界 | `skills/backend/dotnet-webapi-template/skill.md`、`guides/backend/application-service-guide.md`、`guides/backend/dapper-repository-guide.md` | 已链接模板 Skill |
| `backend/dapper-repository` | 新增或重构 Dapper Repository、SQL、分页查询、乐观并发 | `skills/backend/dotnet-webapi-template/skill.md`、`skills/backend/backend-contexts/skill.md`、`guides/backend/dapper-repository-guide.md` | 已链接模板 Skill |
| `backend/dotnet-webapi-crud` | 生成或修改 Web API CRUD、Controller/Service/Repository/DTO | `skills/backend/dotnet-webapi-template/skill.md`、`skills/backend/backend-contexts/skill.md`、`guides/backend/webapi-controller-guide.md` | 已链接模板 Skill |
| `frontend-wpf/dotnet-wpf-toolkitmvvm` | .NET 8+ WPF CommunityToolkit MVVM 页面和 ERP DataGrid 工作流 | `guides/frontend-wpf/wpf-communitytoolkit-mvvm-guide.md`、`guides/frontend-wpf/wpf-datagrid-guide.md` | 既有可用 |
| `frontend-wpf/wpf-datagrid-editing` | WPF DataGrid 编辑列、LineModel、脏数据、快捷键和父 ViewModel 绑定 | `guides/frontend-wpf/wpf-datagrid-guide.md`、`guides/frontend-wpf/wpf-communitytoolkit-mvvm-guide.md` | 新增 |
| `frontend-wpf/wpf-theme-fix` | WPF 主题、资源字典、颜色、样式、HandyControl 兼容修复 | `guides/frontend-wpf/wpf-theme-resource-guide.md`、`guides/frontend-wpf/wpf-custom-controls-guide.md` | 新增 |
| `shared/refit-contract-change` | Shared DTO、Request/Response、Refit API、前后端契约变更 | `skills/backend/dotnet-webapi-template/skill.md`、`guides/shared/refit-api-guide.md`、`guides/shared/dto-guide.md` | 已链接模板 Skill |
| `workflow/safe-refactoring` | 大项目安全重构、分阶段迁移、影响范围分析和行为保护 | `skills/backend/dotnet-webapi-template/skill.md`、`guides/workflow/safe-refactoring-guide.md`、`guides/workflow/project-onboarding-guide.md` | 已链接模板 Skill |

## 标准结构

```text
skill-name/
  skill.md
  README.md
```

已有历史 Skill 可能仍使用 `SKILL.md`、`agents`、`assets`、`references` 结构；新建执行型 Skill 优先使用本阶段的轻量结构。

## 维护规则

- `skill.md` 写触发场景、必读 Guide、工作流、硬规则、禁止事项、验证和输出。
- 不要大段复制 Guide 内容。
- Guide 内容不足时在 Skill 中标记 `TODO：待人工确认`。
- Skill 不承载项目专属业务规则，项目专属内容留在对应 template。
