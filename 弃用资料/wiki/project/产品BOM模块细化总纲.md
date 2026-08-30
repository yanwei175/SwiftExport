# 产品 BOM 模块细化总纲

本文定义 SwiftExport 产品、客户产品、供应商产品、BOM、物料类型和供给方式的细化规则，是销售订单、毛需求、净需求、采购和 PackingList 闭环的基础资料入口。

本文补充 [领域模块总纲](领域模块总纲.md)、[数据模型总纲](数据模型总纲.md)、[ERP 业务总纲](erp-business-blueprint.md) 和 [前端页面总纲](前端页面总纲.md)。源码事实优先于本文。

## 1. 模块目标

产品/BOM 模块要解决三件事：

1. 内部系统到底用哪个 SKU 表达物料。
2. 客户下单语言如何映射到内部 SKU。
3. 一个父物料如何通过 BOM 拆成后续采购、生产、外协或客供件需求。

这个模块只维护基础事实和结构，不直接生成销售订单、采购单、入库单、出库单或库存流水。

## 2. 核心对象

通用产品：

- `Product`
- `ProductCategory`
- `ProductColor`
- `ProductPackage`
- `ProductMaster`
- `ProductDetail`

关系资料：

- `CustomerProduct`
- `SupplierProduct`

BOM：

- `Bom`

主要入口：

- `SwiftExport.Core/Entities/MasterData/Product.cs`
- `SwiftExport.Core/Entities/MasterData/Bom.cs`
- `SwiftExport.Core/Entities/Relations/CustomerProduct.cs`
- `SwiftExport.Core/Entities/Relations/SupplierProduct.cs`
- `SwiftExport.Application/Services/Products/ProductService.cs`
- `SwiftExport.Application/Services/Products/BomService.cs`
- `SwiftExport.Application/Services/Customers/CustomerProductServices.cs`
- `SwiftExport.Contracts/DTOs/Products/**`
- `SwiftExport.Contracts/DTOs/Customers/CustomerProductDto.cs`
- `SwiftExport.View.FW/ProductViews/**`
- `SwiftExport.View.FW/CustomerViews/**`

## 3. 产品主数据职责

`Product` 是内部物料事实，核心业务键是 `Sku`。

产品应维护：

- `Sku`
- 中英文描述。
- 分类、颜色、规格、图片、包装参数。
- `MaterialType`：物料类型，例如成品、配件、客供件。
- `SupplyType`：供给方式，例如采购、生产、外协。
- `BomNo` / `BomVersion`：BOM 入口。
- 默认客户型号、默认供应商等辅助默认值。

产品不应承担：

- 客户专属型号和客户专属描述的全部差异。
- 供应商专属料号和采购价的全部差异。
- 具体销售、采购、入库、出库执行状态。
- 库存余额和库存流水。

## 4. 客户产品职责

`CustomerProduct` 是客户语言到内部 SKU 的映射。

它应维护：

- 客户编码 / 客户 `Uid`。
- 客户 SKU。
- 内部 `Sku`。
- 客户侧描述、单位、条码、包装、价格、默认配置。
- 客户产品和内部产品的联动字段。

它不应维护：

- 通用产品的真实物料属性。
- BOM 结构。
- 采购供应商。
- 库存数据。

销售订单录入时，优先从客户产品选择器进入，再回填内部 `Sku`、客户型号、物料类型、价格和包装字段。

## 5. 供应商产品职责

`SupplierProduct` 表达供应商能提供什么，以及供应商侧默认交易信息。

它应维护：

- 供应商编码 / 供应商 `Uid`。
- 内部 `Sku`。
- 供应商料号。
- 默认采购价、MOQ、交期和供货能力。

它不应维护：

- 客户 SKU。
- 销售订单需求。
- 库存流水。
- 客供件固定 A/B 厂角色。

同一个供应商可以供应成品，也可以供应配件或参与客供件流转。供应商角色由具体净需求和执行单据决定，不写死在供应商主数据里。

## 6. MaterialType 规则

`MaterialType` 表达物料性质，用于业务分支判断。

当前重点：

- 成品：可作为销售订单父物料或出货对象。
- 配件：可作为 BOM 子物料或采购对象。
- 客供件：BOM 子物料的特殊物流对象。

规则：

- 客供件判断来自子物料 `MaterialType`。
- 不把客供件写死为某个供应商的固定角色。
- PackingList 过账时，客供件扣减应通过 BOM、净需求、执行关系和供应商仓定位。
- `MaterialType` 不能替代 `SupplyType`；物料性质和供给方式分开。

## 7. SupplyType 规则

`SupplyType` 表达满足净需求的执行方式。

当前语义：

- 采购：当前阶段重点实现。
- 生产：未来扩展。
- 外协：未来扩展。

规则：

- `SupplyType` 不表示是否有 BOM。
- 是否有 BOM 由 `BomVersion`、`BomNo` 和 BOM 明细决定。
- 净需求阶段根据 `SupplyType` 决定转采购、生产或外协。
- 当前阶段遇到生产/外协需求时，可以展示和保留扩展口，不误转采购。

## 8. BOM 结构规则

BOM 表达父物料到子物料的结构。

推荐业务键：

```text
BomNo + BomVersion + LineNumber
```

BOM 行应保留：

- 父项 `Sku` / `Uid`。
- 子项 `SubSku` / `Uid`。
- 用量、损耗、单位。
- 行号。
- 层级或父子关系辅助字段。

规则：

- BOM 只表达结构，不表达采购执行状态。
- BOM 不写采购单号、入库单号、出库单号。
- BOM 拆解后，执行状态进入毛需求、净需求和 `DemandExecutionLink`。
- 删除或修改 BOM 前要考虑是否已有销售订单、毛需求或净需求引用。

## 9. BOM 版本与变更

BOM 版本用于保证历史需求可追溯。

规则：

- 新销售订单或新毛需求使用当前有效 BOM 版本。
- 已拆解的净需求应保留当时使用的 BOM 信息。
- 修改 BOM 不应静默影响已经确认、拆解或执行中的旧需求。
- 重大 BOM 变更优先新增版本，而不是直接覆盖历史结构。
- 如果只是未使用草稿 BOM，可以允许直接编辑。

后续需要细化：

- BOM 生效日期。
- BOM 停用日期。
- 默认版本选择。
- BOM 复制新版本。
- 已引用 BOM 的变更提示。

## 10. 销售订单使用产品资料

销售订单行优先从客户产品或销售产品选择器带出：

- `CustomerProductId`
- `CustomerSku`
- `Sku`
- 描述、颜色、配置。
- `MaterialType`
- 价格和包装参数。

规则：

- 销售订单行保存的是当时交易快照，不应依赖后续产品资料变化实时改变历史订单。
- 产品资料变化后，新订单使用新资料；旧订单是否更新必须通过明确改单。
- 客户产品映射缺失时，可以允许手工补录，但应提示后续维护客户产品资料。

## 11. BOM 到需求拆解

毛需求拆解时：

1. 找到来源销售订单行或手工需求中的内部 `Sku`。
2. 判断该 `Sku` 是否有有效 BOM。
3. 读取 BOM 明细。
4. 计算子物料净需求数量。
5. 复制子物料的 `MaterialType`、`SupplyType`、默认供应商等关键属性。
6. 生成 `DependentDemand`。
7. 保留父物料、子物料、BOM 层级、来源毛需求、来源单号和来源行。

规则：

- 没有 BOM 的物料可以直接形成自身净需求，具体规则按业务场景确定。
- 有 BOM 的物料应按 BOM 明细拆解。
- 客供件在拆解时识别，但物流执行在采购、入库、PackingList 阶段完成。
- 拆解结果必须可追溯到 BOM 版本。

## 12. 页面设计规则

产品/BOM 页面优先使用管理页 + 明细/子区域模式。

产品管理页：

- 查询、分页、筛选产品。
- 编辑产品基础字段。
- 显示 BOM 入口和版本。
- 支持产品分类、颜色、供给方式、物料类型筛选。

BOM 子区域：

- 围绕选中产品维护 BOM 明细。
- 使用 `BaseLineModel` / `BaseListModel` 承载可编辑行。
- 不把 BOM 明细拆成无业务意义的小 View。

客户产品页：

- 按客户过滤。
- 维护客户 SKU 到内部 SKU 的映射。
- 支持产品选择器联动回填。

搜索选择控件：

- 新场景优先使用 `SearchableTextBoxPlus`。
- 远程搜索用 API 更新候选列表。
- 选中显示值和搜索关键字分离。

## 13. API 设计规则

常见 API 类型：

- 产品分页查询。
- 产品详情。
- 产品 Lookup/Search。
- 客户产品分页查询。
- BOM 按 `BomNo` 查询。
- BOM 保存。
- BOM 校验。

规则：

- Query API 不改变业务状态。
- Save API 不暗中触发需求拆解。
- BOM 校验 API 只校验结构和引用，不生成下游单据。
- DTO 不返回 Entity。
- Refit 路由和 Controller 路由保持一致。

## 14. 验收场景

产品资料：

- 新增产品，保存 `Sku`、`MaterialType`、`SupplyType`。
- 产品分页、搜索、筛选正常。
- 产品修改后不会静默改变历史销售订单行。

客户产品：

- 客户 SKU 可映射到内部 SKU。
- 销售订单选择客户产品后能回填客户型号、内部 SKU、描述、价格和物料类型。

BOM：

- 给成品建立 BOM。
- BOM 明细可保存、编辑、删除。
- BOM 版本和 `BomNo` 能被产品引用。
- 毛需求拆解时按 BOM 生成净需求。
- 客供件子物料能被识别。

## 15. 后续待细化

- BOM 版本生效策略。
- BOM 复制与变更历史。
- 客户产品批量导入。
- 供应商产品页面和采购价策略。
- 产品图片和文件存储策略。
- 无 BOM 产品是否直接形成自身净需求的明确规则。
