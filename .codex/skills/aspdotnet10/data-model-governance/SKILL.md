---
name: aspdotnet10-data-model-governance
description: SwiftExport ERP 数据模型架构治理技能。用于分析 Core Entity 字段归属与冗余、实体职责、逻辑外键与历史快照、Contracts Entity/Query/Command DTO 分类、联表 DTO 字段来源、PostgreSQL 表结构与约束，以及 Dapper Repository 和 Infrastructure 实现路线。适用于数据模型治理、实体设计分析、DTO 设计规划和数据库模型规划；只做设计时不得修改业务代码。
---

# SwiftExport 数据模型治理

## 必读入口

1. `AGENTS.md`
2. `.agents/planner.toml`
3. `.agents/workflows/数据模型治理工作流.md`
4. `.agents/references/业务架构总纲.md`
5. `.agents/references/工程架构总纲.md`
6. `.agents/references/数据模型与状态规则.md`
7. `references/Core实体与关系治理.md`

## 按需读取

- 审查 Contracts DTO 或联表字段：读 `references/DTO分类与字段来源.md`、`../contracts-dto-models/SKILL.md` 和 `../contracts-netstandard20/SKILL.md`。
- 审查 Entity/DTO 映射：读 `../application-mapster-mapping/SKILL.md`。
- 设计表、索引、约束和默认值：读 `references/数据库设计检查清单.md`。
- 规划 Repository 和 Infrastructure：读 `references/Infrastructure实现路线.md`，再按需要读取 `../dapper-repository/SKILL.md`、`../infrastructure-repository-base/SKILL.md`、`../infrastructure-db-session/SKILL.md` 和 `../infrastructure-di-registration/SKILL.md`。
- 修改或重构核心 Entity：读 `references/实体变更影响分析.md`。

## 执行规则

1. 先从业务总纲确认实体存在的业务目的，再读取源码事实。
2. 区分当前事实、目标设计、迁移建议和待确认业务规则。
3. 对每个字段给出保留、改名、移至 DTO、明确为快照、删除候选或待确认结论。
4. 对每个关系说明 Uid、业务编码冗余、历史快照和一致性校验责任。
5. 不默认要求数据库强 Foreign Key；必须说明任何强 FK 建议的收益和副作用。
6. 为联表 Query DTO 记录字段来源、Join 类型/别名、Join 条件、关系基数、关联过滤、实时/快照语义和空值策略。
7. 根据现有接口、实现、DI 和调用方规划 Infrastructure，不凭目录或命名判断已实现。
8. 只给设计时不得修改 Entity、DTO、SQL、数据库脚本或 Repository。

## 最低输出要求

- Entity 字段审计表。
- 实体关系与一致性风险矩阵。
- DTO 分类和联表字段来源表。
- 数据库主键、业务键、索引、唯一约束和默认值建议。
- Repository Interface、实现、DI、调用方和优先级矩阵。
- Entity 变更影响链。
- 可交给 backend Agent 的分阶段任务和待确认问题。
