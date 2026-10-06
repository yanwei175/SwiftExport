# PackingList导入与来源关联

核对日期：2026-10-06。当前导入可先建立待关联来源的草稿；正式过账必须完成来源数量分配，不以Invoice字符串或SKU自动证明来源归属。

1. Excel解析数据通过POST api/for-excel/packing-lists/check检查，后端不读工作簿、不在检查中保存。
2. 草稿通过IPackingListApi的drafts创建/更新。导入检查及DraftService校验客户、标准/非标身份和单位，源输入及可空GrossDemandId以现有DTO为准。
3. 使用sources/search选择真实有效来源，通过drafts/{uid}/lines/{lineUid}/associate-sources明确分配数量。SKU/单号查询只帮助候选检索，不代替稳定来源身份和版本。
4. 仍有待关联行时post-preview/post拒绝。全部关联后由箱单过账服务重验同来源总额度、采购及库存事实，不能沿用导入时检查结果作为正式令牌。
5. 来源反向或取消时检查有效箱单草稿/正式执行，不能仅清空客户端关联逃过生命周期保护。

依据：[箱单Controller](../SwiftExport.WebApi/Controllers/Shipping/PackingLists/PackingListController.cs)、[Draft及关联服务](../SwiftExport.Application/Services/Shipping/PackingLists/PackingListDraftService.Association.cs)、[ForExcel](../SwiftExport.WebApi/Controllers/ForExcel/ForExcelPackingListController.cs)、[能力地图](../.agents/references/模块能力地图/PackingList模块能力地图.md)。当前CR/非标过账只形成交付的边界仍有效。本轮未操作Excel或箱单。
