---
name: aspdotnet10-application-base-service
description: SwiftExport Application 服务基类技能。用于新增或重构继承 IBaseService/BaseService 的 CRUD 服务、GetById/Add/Update/Delete、ValidateBeforeCreate/Update/Delete 校验 hook、SaveItemResult 返回和基础 Repository 写入流程。
---

# Application Base Service Skill

## 必读

- `SwiftExport.Application/Interfaces/IBaseService.cs`
- `SwiftExport.Application/Services/Base/BaseService.cs`
- `../application-service-context/SKILL.md`
- 涉及传输基类时读 `../application-transfer-dtos/SKILL.md`
- 涉及映射时读 `../application-mapster-mapping/SKILL.md`

## 什么时候使用

- 新增标准 CRUD Application Service。
- 修改 `IBaseService<TEntity,TDto>` 或 `BaseService<TEntity,TDto>`。
- 实现新增、修改、删除前的轻量校验。
- 排查 `SaveItemResult`、Version、软删除、RepositoryWriteResult 的写入结果。

## 核心规则

- `IBaseService` 只定义 `GetByIdAsync`、`AddAsync`、`UpdateAsync`、`DeleteAsync`。
- `BaseService` 只承载通用 CRUD，不承载复杂业务命令。
- 复杂业务命令必须在具体 Application Service 中显式命名，例如确认、拆解、转采购、过账、红冲。
- 具体服务可重写：
  - `ValidateBeforeCreateAsync`
  - `ValidateBeforeUpdateAsync`
  - `ValidateBeforeDeleteAsync`
- 校验 hook 返回错误字符串；返回 `null` 表示通过。
- CRUD 映射默认依赖 Mapster，写入结果用 `SaveItemResultFactory` 包装。

## 详细参考

需要实现模板和边界时读：

- `references/服务基类规则.md`
