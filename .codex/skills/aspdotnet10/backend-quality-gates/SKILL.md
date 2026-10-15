---
name: aspdotnet10-backend-quality-gates
description: 为SwiftExport后端行为改动选择定向、模块或阶段验证；保留权限、命令、事务和数据库并发门禁。
---

# Backend Quality Gates Skill

## 必读

- `.agents/references/测试与验收总纲.md`
- `SwiftExport.Application.Tests/SwiftExport.Application.Tests.csproj`
- 目标实现及其现有测试
- 涉及复杂单据时读 `../application-document-command/SKILL.md`

## 核心规则

- 验证范围按风险选择，不以只通过 build 代替行为验证，也不要求每次机械运行全量测试。
- 修改已有行为时先定位对应回归测试；已有覆盖可直接复用，不无条件修改Test文件。Bug修复先有最小复现证据，失败重测与扩大范围按测试总纲执行。
- Controller/Refit/DTO 变化验证路由、HTTP 方法、参数来源、返回类型和序列化契约。
- Repository/SQL 变化验证参数化、稳定排序、列别名、事务传递、Version 条件、锁提示和数据库元数据约束。
- 权限变化验证未认证、无权限、有权限，并确认拒绝路径零业务写入。
- 正式命令至少验证成功、Version 冲突、相同 CommandId 重放、同 ID 不同内容冲突、中途失败回滚和 Session 事务清理。
- Preview/Post 验证 Preview 不写入、过期依据被拒绝、正式提交重新校验。
- 库存和反向命令验证数量守恒、不可变原事实、反向关系、重复执行和每个失败阶段不提交部分结果。
- 测试不得通过删除有效断言、放宽业务条件或依赖测试顺序来迁就实现。

## 验证层级

- Contracts/API：静态或反射契约测试。
- Policy/Builder：纯单元测试覆盖边界和状态矩阵。
- Application：使用可观察事务/仓储替身验证编排、权限、幂等和回滚。
- Repository/Schema：SQL 契约测试；锁、唯一约束或数据库特性有风险时增加真实 PostgreSQL 验证。
- Build：日常构建受影响产品或测试项目；跨模块/共享依赖或阶段验收才选相应解决方案。构建不替代行为证据，同一次定向test的有效构建无需另跑一遍。

## 详细参考

- 选择最小测试集合和报告验证缺口时读 `references/后端风险测试矩阵.md`。

