# Skill: 批量保存事务与业务校验

## Description
当用户涉及“保存数据”、“BatchSaveAsync”、“处理保存前的验证”或“实现 Hook 方法”时使用。

## Instructions
- **事务处理**：批量操作必须包裹在 `_session.Connection.BeginTransaction()` 中。
- **Hook 方法重写**：
  - `ValidateBeforeAddAsync`：用于新增前的重复项检查（如 SKU 唯一性）。
  - `ValidateBeforeUpdateAsync`：用于修改前的状态检查（如已发货禁止修改）。
- **删除策略**：在 Service 中通过 `IsSoftDelete` 属性控制是“硬删除”还是“标记删除”。
- **结果封装**：所有保存操作必须返回 `ServiceResult<BatchResultDto>`。