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
    /// <summary>按四位数量和单价计算四位金额；空价仅供历史读取表达，写入前必须明确两价。</summary>
    public static decimal? Amount(decimal quantity, decimal? price)
    {
        Number(quantity, 4, "采购数量", true);
        if (!price.HasValue) return null;
        Number(price.Value, 4, "采购单价");
        try
        {
            var amount = decimal.Round(checked(quantity * price.Value), 4, MidpointRounding.AwayFromZero);
            Number(amount, 4, "采购金额");
            return amount;
        }
        catch (OverflowException) { throw ProcurementOperationException.Validation("采购金额溢出。", "Lines[].UnitPrice"); }
    }
    /// <summary>创建、保存与确认共用两价必填和四位物理精度校验；明确零价有效，金额仅由后端计算。</summary>
    public static (decimal UnitAmount, decimal TaxAmount) CalculateAmounts(decimal quantity, decimal? unitPrice, decimal? taxPrice,
        string unitPriceField = "Lines[].UnitPrice", string taxPriceField = "Lines[].TaxPrice")
    {
        if (!unitPrice.HasValue)
            throw ProcurementOperationException.Validation("未税单价不能为空；无报价时请明确输入0。", unitPriceField);
        if (!taxPrice.HasValue)
            throw ProcurementOperationException.Validation("含税单价不能为空；无报价时请明确输入0。", taxPriceField);
        Number(unitPrice.Value, 4, unitPriceField);
        Number(taxPrice.Value, 4, taxPriceField);
        return (Amount(quantity, unitPrice)!.Value, Amount(quantity, taxPrice)!.Value);
    }
    /// <summary>预检和正式确认共用已有金额核验，拒绝缺价或金额失真，不能在只读预检中修补数据。</summary>
    public static void RequireConfirmationAmounts(PurchaseOrderLine line)
    {
        var amounts = CalculateAmounts(line.Quantity, line.UnitPrice, line.TaxPrice);
        if (line.UnitAmount != amounts.UnitAmount)
            throw ProcurementOperationException.Validation("采购金额与数量单价不一致，请重新保存采购草稿。", "Lines[].UnitAmount");
        if (line.TaxAmount != amounts.TaxAmount)
            throw ProcurementOperationException.Validation("采购金额与数量单价不一致，请重新保存采购草稿。", "Lines[].TaxAmount");
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
    /// <summary>固定采购根产品必须是活动且采购型的真实物料，包含包材；不得以 SKU 前缀推断非标业务类型。</summary>
    public static void RequireProduct(Product? product)
    {
        if (product is null || !product.IsActive || string.IsNullOrWhiteSpace(product.Sku)
            || product.SupplyType != SupplyTypeEnums.采购
            || product.MaterialType is not (MaterialTypesEnum.成品 or MaterialTypesEnum.零件 or MaterialTypesEnum.客供件 or MaterialTypesEnum.包材))
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
            var line = lines.SingleOrDefault(x => x.Uid == item.PurchaseOrderLineId)
                ?? throw new InvalidOperationException("采购行不属于本订单。");
            if (item.ExpectedVersion <= 0 || item.ExpectedVersion != line.Version)
                throw new ProcurementOperationException(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.VersionConflict, "采购行版本冲突。", "Lines[].ExpectedVersion");
            // 商务补丁显式白名单，绝不映射数量、产品、单位或送达供应商。
            var amounts = CalculateAmounts(line.Quantity, item.UnitPrice, item.TaxPrice);
            line.UnitAmount = amounts.UnitAmount;
            line.TaxAmount = amounts.TaxAmount;
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
