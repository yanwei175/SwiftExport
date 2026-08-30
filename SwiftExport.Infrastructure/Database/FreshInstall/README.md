# SwiftExport 全新数据库初始化与白名单迁移

本目录替代旧的“在原数据库内 DROP 后重建”方案。旧数据库只作为只读数据源；所有建表、补字段、索引和约束都发生在全新目标数据库。

## 执行前约定

- 源库与目标库必须位于同一个 SQL Server 实例。
- 示例源库名为 `dv`、目标库名为 `dv_next`，执行前必须按实际名称修改每个脚本顶部变量。
- 使用具备 `CREATE DATABASE`、跨库 `SELECT` 和目标库 DDL 权限的账号。
- 每个脚本必须整体执行，不能只运行光标所在语句。
- 旧库迁移期间应停止业务写入，或者至少在执行 `010` 后保持只读，避免校验与复制之间发生变化。

## 执行顺序

1. 连接旧库，执行 `010_源库只读预检.sql`。
2. 连接 `master`，执行 `020_创建新数据库.sql`。
3. 连接新目标库，执行 `030_初始化目标业务表.sql`。
4. 仍连接新目标库，执行 `040_迁移业务白名单.sql`。
5. 仍连接新目标库，执行 `050_迁移系统白名单.sql`。
6. 仍连接新目标库，执行 `055_规范化系统字典.sql`。
7. 仍连接新目标库，执行 `060_补充白名单关系与索引.sql`。
8. 仍连接新目标库，执行 `070_迁移后只读检查.sql`。

## 白名单

业务主数据：`CustomerProduct`、`CustomerPoConfig`、`Customer`、`Product`、`ProductCategory`、`ProductColor`、`ProductDetails`、`Supplier`。

系统基础数据：`SysDicCurrency`、`SysDictData`、`SysDictType`、`SysRoles`、`SysUser`、`SysUser_Roles`、`SysViewRoute`。

迁移脚本使用 `SELECT ... INTO` 在新库复制源表的原始列、原始数据和 Uid；不会复制源库索引、默认约束或外键，必要的目标约束由后续脚本显式建立。

`ProductDetails.ProductId`、`CustomerPoConfig.CustomerId/SupplierId` 只在新库新增。能通过唯一活动编码匹配的记录会回填；不能唯一匹配的记录保持 `NULL`，由 `070` 输出 `WARNING`，不会修改或删除原始记录。

## 禁止事项

- 不要在旧库运行 `030` 至 `060`。
- 不要把源库名和目标库名设置成同一个值。
- 不要在新基线后运行历史 003–015 迁移。
- 不要再执行 `Database/Rebuild` 下的旧就地重建流程。
