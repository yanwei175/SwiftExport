---
name: backend-query
description: SwiftExport 后端读取与 Excel 规则。用于列表分页、详情、下拉 lookup、报表、单据导出数据、基础数据模板导出与平表导入。
---

# 查询与导入导出

## 先读

- 样板：`ReferenceDesign/src/SwiftExport.Application/*/*/*Queries.md`
- [SqlConventions](../../../../Handoff/Claude/ReferenceDesign/src/SwiftExport.Infrastructure/Persistence/SqlConventions.md)
- [PagedResult](../../../../Handoff/Claude/ReferenceDesign/src/SwiftExport.Contracts/Common/PagedResult.md)

## 查询

- `[HttpGet]` + `[HasPermission(View)]` → `XxxQueries` → Dapper SQL 直接返回 DTO。不开事务、不加载实体、不做幂等。
- 列表必须分页；禁止循环内查库。
- 下拉：`GET api/{资源}/lookup?keyword=&take=20` 返回 `{ Id, Code, Name }`，只含启用数据。
- 软删除表查询带软删除条件。
- 跨模块的追踪/进度查询属于唯一的订单跟踪模块（OrderTracking），各业务模块不各自实现追踪查询。

## Excel

| 场景 | 技术做法 |
|---|---|
| 单据导出 | 由客户端（VSTO/WPF）生成文件；后端只提供查询 DTO，不新增导出文件接口 |
| 主数据导出 | 服务器模板文件 + 后端填充返回 |
| 导入 | 平表。先校验返回逐行错误不写库；全部通过后整批一个事务提交 |

## 禁止

- 为查询套事务或命令执行器。
- 在查询方法里修改数据。
- 后端为单据新造 Excel 生成逻辑。
