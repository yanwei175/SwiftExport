using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.DTOs.ForExcel.PurchaseOrders;
using SwiftExport.Core.Entities.Procurement;
namespace SwiftExport.Application.Services.Procurement.PurchaseOrders.Draft;

/// <summary>独立采购创建与导入检查共用的输入规则；不读取数据库或计算整单优惠。</summary>
public static class IndependentPurchaseOrderInputPolicy
{
    /// <summary>客户型号仅去首尾空格及忽略大小写，不删除内部空格、符号或前导零。</summary>
    public static string Normalize(string? value) => value?.Trim().ToUpperInvariant() ?? "";

    /// <summary>旧手工请求保留原指纹结构；使用新增快照或非标能力时纳入完整新增输入。</summary>
    public static object FingerprintInput(CreateIndependentPurchaseOrderCommand command)
    {
        if (command.BoNo is not null
            || command.CustomerPoNo is not null
            || command.ReplyDate.HasValue
            || command.SalesPerson is not null
            || command.PurchasePerson is not null
            || command.InletHoseType is not null
            || command.AeratorType is not null
            || command.PalletType is not null
            || command.BoxType is not null
            || command.LabelType is not null
            || command.InstructionManualType is not null
            || command.CartonType is not null
            || command.LogoType is not null
            || command.DiscountRate.HasValue
            || command.DiscountAmount.HasValue
            || command.DiscountRemark is not null
            || command.Lines.Any(x => x.IsNonStandard || x.SelectedCustomerProductId.HasValue
                || x.CustomerSku is not null || x.LinkSku is not null || x.AcceptLinkSkuMismatch
                || x.Unit is not null || x.FinishZh is not null || x.FeaturesZh is not null))
            return new { command.CustomerId,command.SupplierId,command.OrderDate,command.Remark,command.PackagingRequirements,
                command.BoNo,command.CustomerPoNo,command.ReplyDate,command.SalesPerson,command.PurchasePerson,command.InletHoseType,command.AeratorType,command.PalletType,command.BoxType,command.LabelType,command.InstructionManualType,command.CartonType,command.LogoType,command.DiscountRate,command.DiscountAmount,command.DiscountRemark,
                command.Lines };
        // 字段次序和名字与旧持久化回执一致，避免部署后旧命令重试被误判内容冲突。
        return new { command.CustomerId,command.SupplierId,command.OrderDate,command.Remark,command.PackagingRequirements,
            Lines = command.Lines.Select(x => new { x.ProductId,x.Quantity,x.DeliverySupplierId,x.UnitPrice,x.TaxPrice,
                x.PlannedDeliveryDate,x.SupplierSku,x.DescriptionZh,x.SubNote }).ToList() };
    }

    /// <summary>校验单头快照的物理长度与精度，不更改输入。</summary>
    public static void ValidateHeader(CreateIndependentPurchaseOrderCommand command)
    {
        if (command.OrderDate == default)
            throw ProcurementOperationException.Validation("采购日期不能为空。", "Header.OrderDate");
        foreach (var (value, limit, field) in new[]
        {
            (command.BoNo,300,"BoNo"),(command.CustomerPoNo,300,"CustomerPoNo"),
            (command.SalesPerson,500,"SalesPerson"),(command.PurchasePerson,500,"PurchasePerson"),
            (command.InletHoseType,300,"InletHoseType"),(command.AeratorType,300,"AeratorType"),
            (command.PalletType,300,"PalletType"),(command.BoxType,300,"BoxType"),(command.LabelType,300,"LabelType"),
            (command.InstructionManualType,300,"InstructionManualType"),(command.CartonType,300,"CartonType"),
            (command.LogoType,300,"LogoType"),(command.DiscountRemark,500,"DiscountRemark"),
            (command.Remark,2000,"Remark"),(command.PackagingRequirements,2000,"PackagingRequirements")
        }) ProcurementOrderPolicy.Text(value,limit,"Header."+field);
        if (command.DiscountRate is { } rate && (rate < 0 || rate > 999.999999m || decimal.Round(rate,6) != rate))
            throw ProcurementOperationException.Validation("价格浮动率超出 decimal(9,6) 范围或精度。","Header.DiscountRate");
        if (command.DiscountAmount is { } amount && (amount < -99999999999999.9999m || amount > 99999999999999.9999m || decimal.Round(amount,4) != amount))
            throw ProcurementOperationException.Validation("金额调整记录超出 decimal(18,4) 范围或精度。","Header.DiscountAmount");
    }

    /// <summary>检查明细数值和商业文本；非标必须没有产品关联并具描述、单位。</summary>
    public static void ValidateLine(CreateIndependentPurchaseOrderLineCommand line)
    {
        ProcurementOrderPolicy.Number(line.Quantity,4,"Quantity",true);
        _ = ProcurementOrderPolicy.CalculateAmounts(line.Quantity, line.UnitPrice, line.TaxPrice, "UnitPrice", "TaxPrice");
        foreach (var (value, limit, field) in new[]
        {
            (line.CustomerSku,300,"CustomerSku"),(line.LinkSku,300,"LinkSku"),(line.SupplierSku,ProcurementOrderPolicy.SupplierSkuMaximumLength,"SupplierSku"),
            (line.DescriptionZh,ProcurementOrderPolicy.DescriptionZhMaximumLength,"DescriptionZh"),(line.FinishZh,500,"FinishZh"),
            (line.FeaturesZh,500,"FeaturesZh"),(line.Unit,300,"Unit"),(line.SubNote,2000,"SubNote")
        }) ProcurementOrderPolicy.Text(value,limit,field);
        if (!line.IsNonStandard) return;
        if (line.ProductId != Guid.Empty || line.SelectedCustomerProductId.HasValue || line.DeliverySupplierId.HasValue)
            throw ProcurementOperationException.Validation("非标行不能携带产品关联或客供件送达身份。","IsNonStandard");
        if (string.IsNullOrWhiteSpace(line.DescriptionZh))
            throw ProcurementOperationException.Validation("非标采购必须填写描述。","DescriptionZh");
        if (string.IsNullOrWhiteSpace(line.Unit))
            throw ProcurementOperationException.Validation("非标采购必须填写单位。","Unit");
    }

    /// <summary>从当前客户型号全部候选中定位唯一关联，不接受其他客户关联或不可采购产品。</summary>
    public static PurchaseOrderImportProductCandidateDto Resolve(string? customerSku, Guid? selection,
        IReadOnlyList<PurchaseOrderImportProductCandidateDto> candidates)
    {
        var items = candidates.Where(x => Normalize(x.CustomerSku) == Normalize(customerSku)).ToList();
        if (string.IsNullOrWhiteSpace(customerSku) || items.Count == 0)
            throw new ProcurementOperationException(ProcurementErrorCodes.ProductNotMatched,"客户型号未匹配；请修正或明确标记非标。","CustomerSku");
        var selected = selection.HasValue ? items.SingleOrDefault(x => x.CustomerProductId == selection.Value) : null;
        if (selection.HasValue && selected is null)
            throw new ProcurementOperationException(ProcurementErrorCodes.SelectionInvalid,"所选关联不属于当前客户及型号。","SelectedCustomerProductId");
        if (selected is null && items.Count != 1)
            throw new ProcurementOperationException(ProcurementErrorCodes.ProductAmbiguous,"当前客户型号有多个关联，请明确选择。","CustomerSku");
        selected ??= items[0];
        if (!selected.IsUsable || !selected.ProductId.HasValue || selected.ProductId == Guid.Empty
            || string.IsNullOrWhiteSpace(selected.Sku) || string.IsNullOrWhiteSpace(selected.Unit))
            throw new ProcurementOperationException(ProcurementErrorCodes.ProductUnavailable,"关联或产品不可采购，或基础单位缺失。","CustomerSku");
        return selected;
    }

    /// <summary>保留用户商业快照；空字符串代表用户明确留空，只有 null 才使用主数据默认。</summary>
    public static void ApplyCommercial(PurchaseOrderLine line, CreateIndependentPurchaseOrderLineCommand input)
    {
        line.CustomerSku = ProcurementOrderPolicy.Text(input.CustomerSku ?? line.CustomerSku,300,"CustomerSku");
        line.LinkSku = ProcurementOrderPolicy.Text(input.LinkSku ?? line.LinkSku,300,"LinkSku");
        line.SupplierSku = ProcurementOrderPolicy.Text(input.SupplierSku ?? line.SupplierSku,ProcurementOrderPolicy.SupplierSkuMaximumLength,"SupplierSku");
        line.DescriptionZh = ProcurementOrderPolicy.Text(input.DescriptionZh ?? line.DescriptionZh,ProcurementOrderPolicy.DescriptionZhMaximumLength,"DescriptionZh");
        line.FinishZh = ProcurementOrderPolicy.Text(input.FinishZh ?? line.FinishZh,500,"FinishZh");
        line.FeaturesZh = ProcurementOrderPolicy.Text(input.FeaturesZh ?? line.FeaturesZh,500,"FeaturesZh");
        line.SubNote = ProcurementOrderPolicy.Text(input.SubNote,2000,"SubNote");
    }
}
