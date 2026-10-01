> 已淘汰（2026-10-01）：旧表头别名链路仅保留历史兼容；新导入采用 ExcelReadProfile／ExcelReadField 地址配置。前端共享缓存已移除旧映射，旧页面按需直接查询旧 API，保存后不刷新缓存。

# Excel 字段映射配置服务边界

字段映射只保存业务组内“Excel 表头别名 → 稳定目标键”的配置，不承担客户、产品、单据或页面模型的业务校验。

## 字段目录归属

后端 `ExcelFieldCatalog` 与 `GET groups`、`GET {groupCode}/fields` 已移除。客户端通过明确注册的导入模型及属性标记反射生成字段目录，配置页和受控赋值器使用同一来源。未标记属性不允许被解析器赋值；数据库中的键即使格式合法，也不能让客户端绕过模型标记。

`TargetFieldKey` 是稳定逻辑键，例如 `Line.Qty`，不要求与模型上的 `QtyText` 属性同名。现有采购、装箱单及销售映射记录不删除、不迁移；历史种子脚本仅作为旧配置初始化资料，不再代表当前可导入字段目录。

## 后端保留的校验

- 读取要求有效身份，配置创建、更新、删除与整组保存要求 `System.ExcelFieldMapping.Manage`。
- `GroupCode` 为 1 至 40 个字符的 ASCII 标识；`TargetFieldKey` 最长 100 个字符，各点分隔段均为 ASCII 标识。段首允许英文字母或下划线，其余允许字母、数字、下划线。不限制 Header、Line 或业务字段白名单。
- 组码和目标键去除首尾空白，保留大小写。调用者必须始终使用相同稳定组码，例如 `SalesOrder`；不承诺在不同数据库排序规则下自动合并大小写变体。
- 表头按共享 `ExcelHeaderNormalizer` 规范化；原始及规范化表头最长 200 字符，备注最长 500 字符。
- 同组未删除的非空规范化表头唯一；不同表头可指向同一目标字段。停用的非空表头仍参与唯一性约束，历史停用空表头种子可通过整组保存保留。
- 配置更新、删除检查预期版本；整组保存保持既有事务、表头互换、失败回滚及缺席行不删除行为。

这些规则保护配置本身。ForExcel 检查服务负责订单业务检查，草稿保存服务仍负责最终业务校验及写入。

## 保留的 API

`GET /api/excel-field-mappings/{groupCode}`、`POST /api/excel-field-mappings`、`PUT /api/excel-field-mappings/{uid}`、`DELETE /api/excel-field-mappings/{uid}`、`PUT /api/excel-field-mappings/{groupCode}/batch` 保留。

## 数据库索引核查

当前 Core 实体、生成结构脚本及历史初始化脚本均声明 `UX_ExcelFieldMapping_Group_Header` 为筛选 UNIQUE 索引。本次未修改或执行数据库脚本，也未读取运行数据库元数据；索引名称含 UX 并不能证明部署实例已具有唯一性。

部署前可只读检查实际索引：

```sql
SELECT name, is_unique, filter_definition
FROM sys.indexes
WHERE object_id = OBJECT_ID(N'dbo.ExcelFieldMapping')
  AND name = N'UX_ExcelFieldMapping_Group_Header';
```
