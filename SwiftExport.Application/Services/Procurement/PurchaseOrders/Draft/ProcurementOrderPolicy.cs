using System.Security.Cryptography;
using System.Text.Json;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderDetailDto;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Entities.MasterData;
namespace SwiftExport.Application.Services.Procurement.PurchaseOrders.Draft;

/// <summary>采购创建与草稿保存的精度、固定身份和商务白名单规则，不承担数据访问。</summary>
public static class ProcurementOrderPolicy
{
    /// <summary>工厂型号按物理列 nvarchar(300) 限制，创建与商务保存共用。</summary>
    public const int SupplierSkuMaximumLength = 300;
    /// <summary>采购描述按物理列 nvarchar(2000) 限制，创建与商务保存共用。</summary>
    public const int DescriptionZhMaximumLength = 2000;
    /// <summary>对类型化请求生成确定指纹；命令身份、操作和单据也必须纳入调用方输入。</summary>
    public static string Fingerprint<T>(T value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
    /// <summary>拒绝超过 SQL 数值范围或精度的输入，禁止依靠数据库静默舍入。</summary>
    public static void Number(decimal value, int scale, string name, bool positive = false)
    {
        var limit = scale == 6 ? 999999999999.999999m : 99999999999999.9999m;
        if (value < 0 || (positive && value == 0) || value > limit || decimal.Round(value, scale) != value)
            throw ProcurementOperationException.Validation(name + "超出允许范围或精度。", name);
    }
    /// <summary>按数量和价格计算四位金额；先检查输入，明确允许零价。</summary>
    public static decimal? Amount(decimal quantity, decimal? price)
    {
        Number(quantity, 4, "采购数量", true);
        if (!price.HasValue) return null;
        Number(price.Value, 6, "采购单价");
        try
        {
            var amount = decimal.Round(checked(quantity * price.Value), 4, MidpointRounding.AwayFromZero);
            Number(amount, 4, "采购金额");
            return amount;
        }
        catch (OverflowException) { throw ProcurementOperationException.Validation("采购金额溢出。", "Lines[].UnitPrice"); }
    }
    /// <summary>校验文本长度，空白归空，不截断用户输入。</summary>
    public static string? Text(string? value, int maximum, string field)
    {
        var text = value?.Trim();
        if (text?.Length > maximum) throw ProcurementOperationException.Validation(field + "超过长度限制。", field);
        return string.IsNullOrEmpty(text) ? null : text;
    }
    /// <summary>只有活动草稿可以商务保存和首次确认；来源类别不改变采购状态规则。</summary>
    public static void RequireDraft(PurchaseOrderHead head, int version)
    {
        if (version <= 0 || head.Version != version)
            throw new ProcurementOperationException(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.VersionConflict, "采购版本冲突，请刷新。", "ExpectedVersion");
        if (head.ConfirmationStatus != ConfirmationStatusEnum.待确认 || head.LifecycleStatus != PurchaseOrderLifecycleStatusEnum.Active)
            throw new InvalidOperationException("只有活动且未确认订单允许此操作。");
    }
    /// <summary>固定采购根产品必须是活动主档且具采购资格；不得以 SKU 前缀推断非标业务类型。</summary>
    public static void RequireProduct(Product? product)
    {
        if (product is null || !product.IsActive || string.IsNullOrWhiteSpace(product.Sku)
            || product.SupplyType != SupplyTypeEnums.采购
            || product.MaterialType is not (MaterialTypesEnum.成品 or MaterialTypesEnum.零件 or MaterialTypesEnum.客供件))
            throw new InvalidOperationException("产品不存在、停用或不具备标准采购资格。");
    }
    /// <summary>保存白名单精确作用于原有行集合，拒绝增删行、重复行和版本冲突。</summary>
    public static void ApplySave(PurchaseOrderHead head, List<PurchaseOrderLine> lines, UpdatePurchaseOrderDraftCommand command)
    {
        RequireDraft(head, command.ExpectedVersion);
        if (command.Lines is null || lines.Count == 0 || command.Lines.Count != lines.Count
            || command.Lines.Any(x => x is null) || command.Lines.Select(x => x.PurchaseOrderLineId).Distinct().Count() != lines.Count)
            throw new InvalidOperationException("必须提交全部原有采购行，不能增加或删除。");
        foreach (var item in command.Lines)
        {
            if (!item.UnitPrice.HasValue)
                throw ProcurementOperationException.Validation("未税单价不能为空；无报价时请明确输入0。", "Lines[].UnitPrice");
            var line = lines.SingleOrDefault(x => x.Uid == item.PurchaseOrderLineId)
                ?? throw new InvalidOperationException("采购行不属于本订单。");
            if (item.ExpectedVersion <= 0 || item.ExpectedVersion != line.Version)
                throw new ProcurementOperationException(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.VersionConflict, "采购行版本冲突。", "Lines[].ExpectedVersion");
            // 商务补丁显式白名单，绝不映射数量、产品、单位或送达供应商。
            line.UnitAmount = Amount(line.Quantity, item.UnitPrice);
            line.TaxAmount = Amount(line.Quantity, item.TaxPrice);
            line.UnitPrice = item.UnitPrice; line.TaxPrice = item.TaxPrice;
            line.SupplierSku = Text(item.SupplierSku, SupplierSkuMaximumLength, "供应商型号");
            line.DescriptionZh = Text(item.DescriptionZh, DescriptionZhMaximumLength, "采购描述");
            line.SubNote = Text(item.SubNote, 2000, "行备注");
            line.PlannedDeliveryDate = item.PlannedDeliveryDate;
        }
        head.Remark = Text(command.Remark, 2000, "采购备注");
        head.PackagingRequirements = Text(command.PackagingRequirements, 2000, "包装要求");
        head.ZheKouBeiZhu = Text(command.DiscountRemark, 2000, "折扣备注");
        head.ExpectedDate = lines.Min(x => x.PlannedDeliveryDate);
    }
}
