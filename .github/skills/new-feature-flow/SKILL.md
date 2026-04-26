# Skill: 新业务功能全栈开发流程

## Description
当用户需要“新建功能模块”、“添加业务实体”、“从数据库表开始开发”或“实现 CRUD 流程”时使用此技能。

## Instructions
请引导用户按照以下 10 个标准步骤完成开发，确保每层继承关系正确：

1. **Core 层**：创建实体继承 `BaseEntity`，定义仓储接口继承 `IRepository<TEntity>`。
2. **Contracts 层**：创建 Dto 继承 `BaseDto`。
3. **Application 层**：实现 Service 及其接口，继承 `BaseService<TEntity, TDto>`。
4. **Infrastructure 层**：实现仓储类，继承 `DapperRepository<TEntity>`，依赖 `RepositoryBaseContext`。
5. **依赖注入**：在 Application 和 Infrastructure 的 `DependencyInjection` 类中注册接口。
6. **WebApi 层**：创建 Controller 继承 `BaseController<TEntity, TDto>`。
7. **Refit 定义**：在 Contracts 层建立接口继承 `IGenericApi<TDto>`。
8. **View.FW 注册**：在 `SwiftExport.View.FW.DependencyInjection` 的 `AddViewApis` 中注册 API 地址。
9. **View.FW Model**：创建 UIModel (`BaseLineModel`) 和 ListModel (`BaseListModel`)，配置 Mapster 映射。
10. **联动修改**：修改字段时，必须同步更新 Entity, Dto, Model 及 Mapping。