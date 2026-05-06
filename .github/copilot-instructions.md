# SwiftExport 全局开发指南

## 核心环境与编码规范
- **语言与编码**：中文项目，源码文件必须固定使用 **UTF-8** 编码。
- **注释要求**：必须添加详细的中文注释，尤其是每个方法和逻辑区域。禁止删除现有注释，修改代码时应补充注释。
- **技术栈**：
  - 后端：.NET 10 (WebApi, Core, Infrastructure, Application)
  - 前端：WPF (.NET Framework 4.8 / C# 7.3)
  - 核心库：Dapper (数据访问), Mapster (对象映射), Refit (API客户端), MateWpfThemes.FW (UI主题)。

## 关键业务规则
- **ERP 域模型**：`MasterSku` 是非交易性父标识（共享属性）；业务操作基于 `Sku` 变体（价格/饰面）。`Sku` 可脱离 `MasterSku` 存在，且拥有独立的 BOM 数据。
- **销售订单逻辑**：
  - 删除：已持久化的行标记 `Deleted`（隐藏），未保存的行直接移除。
  - 金额：行级小计（NwSubTotal等）允许用户手动修改，汇总字段为只读。
- **需求池领域命名**：统一使用 **Supplier** 语义（替代 **Vendor**），并在需求池 DataGrid 第一列保留图片列（MainImage 后端联表返回）。
- **BOM 拆解逻辑**：`BomService` 的 BOM 拆解逻辑应以 `List<IndependentDemand>` 作为核心入参，不依赖 `DecomposeRequestDto` 作为输入模型。

## 交互与输出
- 除非明确要求生成文档，否则直接输出代码，减少 MD 解释。
- 可以提交 Git，但提交信息（Commit Message）必须使用中文。