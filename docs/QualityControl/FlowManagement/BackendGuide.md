# 品质管理／流量管理后端

流量模块保存独立的物料测试组，每组由 `FlowHeader` 和 `FlowLine` 两个实体组成。物料关联 `Product.Uid`，使用 `MaterialId` 和建档时的 `SkuSnapshot`，没有数据库外键。代码分别位于各层的 `QualityControl/FlowManagement`，本阶段没有前端页面。

## 记录与单位

- 头保存测试日期、市场说明、公共配件配置说明和备注，物料及 SKU 快照创建后固定。
- 一组允许测试部分功能，每个功能最多一行，各功能可使用不同限流器。
- 行使用 `FunctionId` 和 `LimiterSpecificationId` 选择公共字典项；显示名由后端生成并保存快照。
- `Flow1BarLMin`～`Flow8BarLMin`：分别记录 1～8 bar 下的 L/min。
- `Flow20PsiGpm`、`Flow40PsiGpm`、`Flow60PsiGpm`、`Flow80PsiGpm`、`Flow100PsiGpm`：记录对应 PSI 下的美国加仑/分钟。
- 同组可填任一套或两套压力列；空表示未测，0 表示实测零值；不换算或推算结果。
- 流量使用项目标准 `numeric(18,4)`。保存拒绝负值、超过四位小数或超出类型范围的值，不静默舍入。
- 每组至少一行，每行至少填写一个测量点，排序号非负。补充文字最大长度沿用实体元数据约定。
- 实际换配件重新测试或相同条件再次实测，都应新建测试组；普通修改用于纠正已有记录。

## 字典

| 字典编码 | 初始项 | 默认显示 |
|---|---|---|
| `FlowTestFunction` | `Default` | Default |
| `FlowLimiterSpecification` | `Unlimited` | 不限流 |

上述两个默认项由 Application 的 `BuiltInDictionaryCatalog` 提供。数据库缺失时读取仍可用且不写库；首次修改显示信息时按固定身份保存覆盖。已有记录保留原 Guid 和版本，历史名称快照不随字典改名而更新。

其他功能、规格由现有字典管理维护。名称取中文优先，中文为空时取英文。限流器标称规格不是实测结果，具体型号可写入行备注。

新建或更换选择只接受对应分类下未删除且启用的项。未变更的既有选择允许继续编辑结果，并保留原快照，即使字典后来改名、停用或删除。历史查询不依赖当前字典或物料资料。

## API

Controller 与站点根 Refit `IFlowApi` 使用相同路径，全链传递取消令牌。

| 方法与路径 | 输入 | 成功数据 |
|---|---|---|
| POST `/api/quality-control/flows/search` | `FlowSearchRequest` | `PagedResponseBase<FlowListItemDto>` |
| GET `/api/quality-control/flows/{id}` | 头 Uid | `FlowDetailDto` |
| POST `/api/quality-control/flows` | `CreateItemRequest<FlowEditDto>` | `FlowSaveResponse` |
| PUT `/api/quality-control/flows/{id}` | `UpdateItemRequest<FlowEditDto>` | `FlowSaveResponse` |
| DELETE `/api/quality-control/flows/{id}` | 请求体 `DeleteItemRequest` | `SaveItemResult` |

统一包裹 `ServiceResult<T>`，失败携带稳定 `Error.Code`。更新和删除的路径 Id 必须等于请求体 Uid。分页支持 `MaterialId`、`SkuSnapshot`、含上下界的 `TestDateFrom/TestDateTo`；SKU 按字面子串搜索，未提供时沿用 `Keyword`。分页每页 1～500 条，按测试日期、Uid 倒序稳定排列。

新增行的 Uid 为全零且 Version 为 0；更新已有行必须携带读取到的 Uid、Version。头身份、SKU、字典名称、审计字段及删除标记不属于可编辑输入。

更新提交 `Data.Lines` 的完整最终集合，遗漏旧行软删除。更换功能的旧行按替换处理，分配新行身份并保留旧行历史，支持同组功能交换；返回 `Detail` 包含最终行身份、快照和版本。任何行变更均推进头 Version，过期头或行版本返回 `CONCURRENCY_CONFLICT`。删除固定软删，不接受通过 `IsSoftDelete=false` 清除历史。

Application 独占本用例 ReadCommitted 事务，维护先锁头、再按行身份锁行；采用新快照时共享锁定物料和字典。任何写入失败或提交前取消都回滚全部头行。提交阶段异常返回 `OUTCOME_UNKNOWN`，应查询核实结果后再操作，不能直接重复新增。本模块不提供 CommandId 幂等重放。

## 权限与数据库交付

权限分别为 `QualityControl.Flow.View/Create/Edit/Delete`，已登记到品质管理权限目录并接入全局权限过滤器和 Application 校验。

1. 对已有当前 PostgreSQL 基线的目标库，先评审并显式执行 `FlowSchema.sql`。它只创建流量两表及索引、CHECK、身份保护和说明；已有流量表时拒绝执行，禁止用整库生成基线升级已有库。
2. 当前版本无需执行 `FlowDictionarySeed.sql` 初始化字典。该脚本仅保留供旧版本兼容，不自动执行，也不覆盖已有名称、启用状态或人员改动。
3. 通过现有 `_tools/DbSeed/同步权限目录.ps1` 显式同步代码权限目录，再由管理员为实际角色授权。脚本不自动授予权限。

脚本均未自动部署或写入运行数据库。后端离线测试和生成基线验证不能替代真实 PostgreSQL 的脚本重入、事务、锁和并发验收；真实写入验证须先明确隔离实例、库名和初始化／清理范围。

## 本次验证记录（2026-10-08）

- 后端解决方案 `dotnet build .\SwiftExport-WebAPI.slnx --no-restore --verbosity quiet` 成功，0 个错误；NuGet 漏洞数据源不可达产生 NU1900 警告。
- Application.Tests 定向回归共 88 个测试通过，0 失败、0 跳过。覆盖 FlowServiceTests、FlowContractTests、FlowHttpTests、FlowRepositoryTests、PermissionCatalogConsistencyTests、EndpointPermissionFilterTests 和 EntityDrivenSchemaContractTests。
- HTTP 测试启动本地测试宿主验证五类路由、身份认证、权限和路径／请求体标识一致性；业务和 Dapper 测试使用离线夹具验证规则、快照、版本、回滚、SQL 参数和事务传递。
- SchemaGenerator 的 `--verify` 校验通过，结构预览与实体元数据一致；交付测试检查增量两表与预览一致，以及初始化脚本的重复执行保护结构。
- 未连接运行库或隔离库，未执行两份 SQL，也未执行权限同步。离线夹具的事务与并发验证不代表真实 PostgreSQL 验收完成。
