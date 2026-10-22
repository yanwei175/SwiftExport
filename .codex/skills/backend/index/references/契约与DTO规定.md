# 契约与 DTO 规定

由旧 `aspdotnet10` Contracts/DTO/Mapster 技能抽取（决策 #23）。以下内容已有权威来源，本文不重复：
- netstandard2.0 语法限制：见 [Guide §16](../../../../../Handoff/Claude/Backend-Architecture-Guide.md)。
- 命名：见 [Contracts README](../../../../../Handoff/Claude/ReferenceDesign/src/SwiftExport.Contracts/README.md)。旧技能的 `ConfirmXxxCommand`、`XxxDetailDto` 等命名与 README 冲突时，以 README 为准。
- System.Text.Json 与 Mapster 用法：见 [Toolbox](../../../../../Handoff/Claude/ReferenceDesign/Toolbox.md)。

## DTO 设计边界

- DTO 按 **API 用例**设计，不按页面、表或字段集合设计。一个页面可以消费多个 DTO，不合并成页面万能 DTO；页面交互状态放客户端 Model，不进 Contracts。
- 业务 DTO 只有同时满足以下全部条件才复用，否则新建专用 DTO：
  1. 同一业务概念，并且在各消费者中承担同一角色。
  2. 共享字段的含义、单位、空值、默认值、精度一致。
  3. 数据来源、权限过滤、敏感字段暴露一致。
  4. 校验、必填、并发字段与演进节奏一致。
  5. 当前已有至少两个真实消费者。
  6. 一方变化不会迫使另一方接收无关字段。
- 必须新建的情形：
  - 列表、详情、导出、追踪、打印的字段口径不同。
  - 查询输出与命令输入方向不同。
  - 权限或脱敏不同。
  - 同名字段语义不同。
  - 复用需要增加大量可空字段或模式标记。
- 禁止 `CommonDto`、`SharedDto`、`BaseXxxDto` 这类只为减少重复、没有稳定业务抽象的类型。
- 新建或复用前须搜索目标模块已有 DTO、Refit、Controller、Service、映射和客户端调用方，并在计划中记录复用或新建的理由。修改现有 DTO 前列出全部调用方；无法确认兼容时新建 DTO。拆分现有 DTO 前建立字段处置矩阵，调用方迁移并验证之前不删除仍需要的字段。
- 业务动作用显式命令请求表达，不伪装成普通 Update；需要并发控制时携带 `Version`。批量命令须返回逐项结果。

## 文件组织

- 一个公开顶级类型一个 `.cs`，文件名与类型名一致。禁止 `XxxDtos.cs`、`XxxModels.cs`、`XxxCommands.cs` 这类聚合桶文件。
- 历史聚合文件“触碰即治理”：只拆本次新增或实质修改的类型。

## 契约一致性

- DTO 不返回 Entity，不承载 UI 状态（`IsSelected`、`IsDirty` 等）。
- Refit 接口路由、HTTP 方法、参数与 Controller 完全一致；改路由须同步 WPF/VSTO/Web 调用点（决策 #10）。
- 已发布枚举成员的数值不得改变；前端可见枚举放 Contracts。
- 错误码保持稳定，客户端按错误码分支，不解析中文消息。
- DTO 字段改名须检查 Dapper 别名、映射配置、JSON、Refit 与客户端绑定。

## 映射

- 后端：Mapster 只做同名平面复制，配置集中（`TypeAdapterConfig`）；禁止用 `Adapt` 生成或修改实体（Toolbox）；不在映射配置里写业务逻辑，不在 Controller 或 Repository 中写映射。查询由 Dapper 直接映射到 DTO。
- 客户端（WPF）：DTO 与页面 Model/LineModel 的结构性复制默认用 Mapster，配置放 `SwiftExport.Client.Wpf/Mappings/**` 或 Feature 内明确的 `Mappings` 位置，先核对现有全局与 Feature 配置；字段名不同、枚举、嵌套、集合明细写显式配置；不新增大段逐字段复制或只包装复制的 Mapping 类；业务计算与状态决策保留显式代码；UI 状态不得映射进 DTO。
