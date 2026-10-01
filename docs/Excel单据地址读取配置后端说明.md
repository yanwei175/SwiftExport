# Excel 单据地址读取配置后端说明

## 范围与约定

本模块为 VSTO 单据页面内的字段设置按钮提供共享配置持久化。实际单元格选择、Excel COM 读取、数据转换、客户或产品匹配、编辑模型赋值和业务 DTO 组装均在客户端完成。后端不接收工作簿文件、不执行 Excel 操作，不依赖 ClosedXML。

配置全员共享，已登录用户可读取；创建、修改和删除复用现有 `System.ExcelFieldMapping.Manage` 权限。此模块不修改销售、采购单据的业务保存规则。

旧 `ExcelFieldMapping` 保存“表头别名到字段”的关系，与地址定位语义不同；本期保留旧表、API 和调用方。客户端接入新规则并完成验证后，另行移除旧入口。不能把旧表头文字直接转换成单元格地址。

## 实体与数据库

两张表均继承现有 BaseEntity，包含 Uid、Version、IsDeleted 和审计字段。

| 表 | 业务字段 | 职责 |
|---|---|---|
| ExcelReadProfile | GroupCode、ConfigName、EndMode、LastRowColumn、SummaryCellAddress、Remark | 一份单据布局配置及唯一的明细结束边界 |
| ExcelReadField | ProfileUid、TargetFieldKey、WorksheetName、CellAddress、IsDownward | 一个字段的位置及方式，WorksheetName 为可空参考信息 |

GroupCode 使用客户端注册的稳定导入分组，创建后不可修改。TargetFieldKey 是稳定字段路径，不是任意 CLR 属性反射入口；服务端验证格式，客户端必须核对其字段目录和允许赋值边界。配置不得写入工作簿绝对路径、密码或实际单据数据。

活动配置的 `GroupCode + ConfigName` 唯一；活动字段的 `ProfileUid + TargetFieldKey` 唯一。名称允许软删除后复用。ProfileUid 为逻辑关系，不生成数据库 FOREIGN KEY。

数据库结构以实体元数据为权威，由现有 SchemaGenerator 生成 `SwiftExport.Infrastructure/Database/Generated/SwiftExport.Schema.sql`。完整基线只能用于已创建的空库，不能在既有业务库执行。

用户后续明确要求提供建表及必要初始化数据，因此另提供 `SwiftExport.Infrastructure/Database/Excel单据地址读取配置建表与初始化.sql`，仅为既有库创建这两个新表，插入 SalesOrder、PurchaseOrder 各一份默认空配置。其结构、索引、CHECK 和中文描述均提取自实体生成基线。该脚本不是通用旧库升级器：不修改其他表，已存在的目标表列结构不兼容时拒绝执行，不迁移旧表头配置。脚本不插入任何字段地址，不覆盖用户修改，也不恢复已软删除的默认配置；默认配置需由客户端补齐后才能读取。本次未连接业务库执行，使用时先在 SSMS 选择目标业务数据库。

## 读取规则

每个映射必须有合法的单格 A1 地址；允许 `$a$8`，持久化为 `A8`。拒绝范围、命名区域、公式、外部引用和越界地址；上限为 XFD1048576。WorksheetName 为可选参考信息，可省略、为 null 或空白，保存时空白归一化为 null；提供名称时仍检查名称格式和 31 字符长度。名称不用于决定前端读取源，也不参与向下字段的名称一致性检查。当前每条明细占一行，不支持跨行记录或多个独立明细区域。

前端实际读取使用 ActiveSheet（不是 ActivateSheet）。一次读取开始时捕获目标 Excel 窗口中的活动工作簿和 Worksheet，整次读取使用这个对象，避免用户切换工作表后出现混合数据。若当前是图表页等非 Worksheet 对象，客户端拒绝读取并提示选择工作表。以上为前端接入约定，本次后端调整不代表前端已实现。

- IsDownward=false：读取指定单元格一次。若客户端把它用于明细常量字段，是否复制到各行由客户端模型决定。
- IsDownward=true：CellAddress 为首条明细位置。所有向下字段必须共用起始行；客户端从本次捕获的活动工作表按整行组装记录，不能让各列独立遇空停止。

EndMode 的固定协议：

| 值 | 含义 | 对应参数 |
|---|---|---|
| 0 | 尚未指定结束方式 | 两个边界参数均为空 |
| 1 | 指定列在起始行及之后的最后有值行 | LastRowColumn，例如 A |
| 2 | 指定汇总单元格所在行之前 | SummaryCellAddress，例如 D30 |

边界作用于本次捕获的活动工作表，不使用保存的参考名称查找 Sheet。选择末行列时必须确保明细以下该列没有合计或备注；不能以 UsedRange 推断明细结束。汇总地址为固定位置，插入或删除行后应重新指定；本期不隐含支持 Excel 命名区域跟踪。公式结果为空、合并单元格、隐藏行、内部空行等实际工作簿读取行为应在客户端实施时明确，不由后端配置完整性推断。

## DTO 与 API

Contracts 保持 netstandard2.0，使用普通 class 和 C# 7.3 可消费的属性。

| DTO | 用例 |
|---|---|
| ExcelReadProfileListItemDto | 配置选择列表 |
| ExcelReadProfileDto | 完整头行快照、CanRead、Issues；包含公共审计信息 |
| ExcelReadFieldDto | 稳定字段映射，查询和更新共享同一语义 |
| ExcelReadProfileIssueDto | 结构完整性缺项的 Code、Field、Message |
| CreateExcelReadProfileRequest | 新建空配置，含 GroupCode、ConfigName、Remark |
| UpdateExcelReadProfileRequest | 完整快照更新，含 ExpectedVersion、名称、结束规则、备注及 Fields |

Refit 入口为 `IExcelReadProfileApi`，BaseAddress 使用 WebApi 根地址。

| HTTP | 路径 | 用途 |
|---|---|---|
| GET | /api/excel-read-profiles?groupCode=PurchaseOrder | 分组配置列表 |
| GET | /api/excel-read-profiles/{uid} | 详情及配置完整性 |
| POST | /api/excel-read-profiles | 创建空配置 |
| PUT | /api/excel-read-profiles/{uid} | 完整快照更新 |
| DELETE | /api/excel-read-profiles/{uid}?expectedVersion=3 | 整份软删除 |

所有接口返回 ServiceResult，透传 CancellationToken。列表和详情只返回未删除记录。没有上传 Excel、读取 Excel 或提交业务单据的 API。

更新请求必须包含完整 Fields 列表：缺席字段表示清除，空列表表示清除全部字段，null 拒绝保存。字段设置框保存时，应修改当前完整配置快照并提交 PUT，不能只发送本次修改的一行。新建配置默认没有字段，不生成大量空映射。

允许分步保存格式合法但结构不完整的配置；详情与保存响应中的 CanRead=false、Issues 表示不能开始读取。存在向下字段时，必须指定一种结束方式、填写对应边界，并保持起始行一致；不要求填写工作表名称或保持参考名称一致。没有字段也不可读取。

CanRead 仅表示已保存配置的结构足够，不证明客户端认识全部 TargetFieldKey，也不证明活动工作表可用、必填业务字段齐全或 Excel 中的数据有效。客户端读取前必须使用当前快照重新校验，并自行检查实际工作簿。配置列表只需 groupCode，详情只需 uid，两个读取配置 API 均不要求传入 WorksheetName。

## 并发、事务与权限边界

全部字段变更以主配置 ExpectedVersion 为唯一对外并发依据。更新或删除先锁定主配置，再读取及修改字段；主表和字段在同一事务提交，成功后主版本递增。旧版本返回 VERSION_CONFLICT，不静默覆盖。

更新按字段键保存差异，保留未清除字段的内部 Uid；缺席字段软删除。清除后重新添加同一字段会创建新记录，不恢复旧记录。独立字段写入接口不对外开放。

详情使用短 RepeatableRead 事务获得一致头行快照；仓储查询不使用 NOLOCK。异常、取消和后半段字段失败必须回滚；事务清理不接管调用方已有事务。所有 SQL 参数化，审计由 Infrastructure 打戳，Controller 仅转发。

配置名称冲突代码为 EXCEL_READ_DUPLICATE_NAME；格式错误使用 VALIDATION，缺项代码见 Issues。错误展示取 Message，客户端分支使用稳定 Code。

## 验证入口

```powershell
dotnet build .\SwiftExport-WebAPI.slnx --no-restore
dotnet run --project .\_tools\SchemaGenerator\SchemaGenerator.csproj --no-restore -- --verify
dotnet test .\SwiftExport.Application.Tests\SwiftExport.Application.Tests.csproj --no-restore --filter "FullyQualifiedName~ExcelReadProfile|FullyQualifiedName~EndpointPermissionFilterTests|FullyQualifiedName~EntityDrivenSchemaContractTests"
```

真实数据库测试需设置 SWIFTEXPORT_RUN_LOCALDB_TESTS=1；测试自行创建带随机 Guid 的临时 LocalDB 空库，执行真实生成基线，完成后只清理该测试库。

## 本次交付验证（2026-10-01）

- WebAPI 解决方案 Debug 构建通过，0 错误；现有 NuGet 漏洞服务不可达，产生 NU1900 警告。
- Contracts 以 LangVersion=7.3 构建通过，0 警告、0 错误。
- SchemaGenerator --verify 通过，提交基线与实体元数据一致。
- 上述专项加旧 ExcelFieldMapping 回归共 35 项全部通过，其中包含实际 LocalDB 空库执行生成基线、名称唯一性、两请求主版本竞争、字段软删除、主配置与字段一起软删除、字段失败回滚、SQL 请求取消回滚及审计。
- SQL 测试在沙箱外运行；沙箱不能自动启动 LocalDB。首次失败发现并修复 NOCOUNT 下影响行数误判，以及 SQL 取消可能以 SqlException 返回时未按请求取消透传的问题。
- 没有执行全量后端测试，没有部署 WebAPI，没有修改或升级既有业务数据库，没有进行 Excel 实机或 VSTO 页面接入验证。
- 后续建表初始化脚本的隔离 LocalDB 专项测试通过：首次建表、27 条中文描述、两个默认空配置、重复执行保持 Uid、不覆盖已编辑的版本与边界、不恢复软删除、不兼容列拒绝执行及事务回滚均已验证。该专项新增 1 项，不将其表述为重新运行全部 35 项。

新文件位于 Core/Entities/Systems/ExcelReadProfiles、Core/Interfaces/Systems/ExcelReadProfiles、Contracts/DTOs/SystemDtos/ExcelReadProfiles、Contracts/Apis/SystemApis/IExcelReadProfileApi.cs、Application/Interfaces/Systems/ExcelReadProfiles、Application/Services/Systems/ExcelReadProfiles、Infrastructure/Repositories/Systems/ExcelReadProfiles 和 WebApi/Controllers/Systems/ExcelReadProfiles。DI、权限过滤器和相关权限回归断言只增加新配置接线，保留本次开始时已有的未提交改动。

## 工作表名称可选调整（2026-10-01）

- WorksheetName 列改为 nvarchar(31) NULL，实体、DTO 注释、服务空值归一化和初始化脚本同步调整。
- 保存允许省略名称；向下字段参考名称不同也不影响 CanRead，同一起始行和结束边界检查仍保留。
- 用户选择自行删除 ExcelReadField 和 ExcelReadProfile 后重建。删除后执行新版“Excel单据地址读取配置建表与初始化.sql”，会创建新结构并插入两份默认空配置；本脚本不自动删表，也不会保留已删表中的旧数据。
- 本次没有替用户连接业务库或删表，也没有修改 VSTO 实际读取代码。
- WebAPI 构建及 SchemaGenerator --verify 通过；新旧映射、权限、结构、初始化和真实 LocalDB 专项共 37 项全部通过，包括名称为 null 的持久化及回传，以及不同参考名称不阻断读取结构检查。


## 前端旧缓存退出（2026-10-01）

旧表头别名模块保留并标注淘汰。VSTO 的 ReferenceDataCacheService 已移除旧映射；历史页面直接查询旧 API，保存不刷新共享缓存。新地址配置的前端读取尚未接入，本次没有数据库变更。
