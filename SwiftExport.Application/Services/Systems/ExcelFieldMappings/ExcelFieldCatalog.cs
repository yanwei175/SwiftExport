using SwiftExport.Contracts.DTOs.SystemDtos.ExcelFieldMappings;

namespace SwiftExport.Application.Services.Systems.ExcelFieldMappings;

/// <summary>维护 Excel 导入的稳定分组及可写 DTO 字段目录；身份、版本和服务端计算字段不进入目录。</summary>
public static class ExcelFieldCatalog
{
    private static readonly (string Code, string Name, (string Key, string Name, bool Create, bool Update)[] Fields)[] Groups =
    [
        ("SalesOrder", "销售订单", [
            Field("Header.InvoiceNo", "发票号"), Field("Header.CustomerPoNo", "客户采购单号"),
            Field("Header.ShippedBy", "运输方式"), Field("Header.Currency", "币种"),
            Field("Header.ExchangeRate", "汇率"), Field("Header.ShipToName", "收货人"),
            Field("Header.ShipToAddress", "收货地址"), Field("Header.ShipToPhone", "收货电话"),
            Field("Header.Incoterm", "贸易条款"), Field("Header.IncotermLocation", "贸易条款地点"),
            Field("Header.Remark", "备注"), Field("Header.OrderDate", "订单日期"),
            Field("Header.DeliveryDate", "交货日期"), Field("Header.Etd", "预计离港日期"),
            Field("Header.Eta", "预计到港日期"), Field("Line.CustomerSku", "客户型号"),
            Field("Line.DescriptionEn", "英文描述"), Field("Line.ColorNameEn", "英文颜色"),
            Field("Line.FeaturesEn", "英文特性"), Field("Line.Qty", "数量"),
            Field("Line.CnyPrice", "人民币单价"), Field("Line.UsdPrice", "美元单价"),
            Field("Line.PcsOfCarton", "每箱数量"), Field("Line.NwOfCarton", "每箱净重"),
            Field("Line.GwOfCarton", "每箱毛重"), Field("Line.CbmOfCarton", "每箱体积"),
            Field("Line.ExpectedDeliveryDate", "预计交货日期"), Field("Line.SubNote", "行备注")]),
        ("PurchaseOrder", "采购订单", [
            Field("Header.OrderDate", "采购日期", create: true, update: false),
            Field("Header.Remark", "备注"), Field("Header.PackagingRequirements", "包装要求"),
            Field("Header.DiscountRemark", "折扣备注", create: false, update: true),
            Field("Line.Quantity", "数量", create: true, update: false),
            Field("Line.SupplierSku", "供应商型号"), Field("Line.DescriptionZh", "中文描述"),
            Field("Line.UnitPrice", "未税单价"), Field("Line.TaxPrice", "含税单价"),
            Field("Line.PlannedDeliveryDate", "计划交货日期"), Field("Line.SubNote", "行备注")]),
        ("PackingList", "装箱单", [
            Field("Header.CustomerNameSnapshot", "客户名称"), Field("Header.CustomerAddressSnapshot", "客户地址"),
            Field("Header.AttentionSnapshot", "收件人"), Field("Header.InvoiceDate", "单据日期"),
            Field("Header.ContainerNo", "集装箱号"), Field("Header.ShippingLocationSnapshot", "出货地点"),
            Field("Header.SealNo", "封条号"), Field("Header.Note", "备注"),
            Field("Header.Etd", "预计离港日期"), Field("Header.Eta", "预计到港日期"),
            Field("Line.CustomerSkuSnapshot", "客户型号"), Field("Line.DescriptionEnSnapshot", "英文描述"),
            Field("Line.ColorNameEnSnapshot", "英文颜色"), Field("Line.Qty", "数量"),
            Field("Line.PcsPerPackage", "每包数量"), Field("Line.NwPerPackage", "每包净重"),
            Field("Line.GwPerPackage", "每包毛重"), Field("Line.CbmPerPackage", "每包体积"),
            Field("Line.CustomerPoNo", "客户采购单号"), Field("Line.InvoiceNo", "发票号"),
            Field("Line.Marks", "箱唛"), Field("Line.Note", "行备注")])
    ];

    private static (string Key, string Name, bool Create, bool Update) Field(string key, string name, bool create = true, bool update = true)
        => (key, name, create, update);

    /// <summary>返回全部稳定分组供维护界面选择。</summary>
    public static IReadOnlyList<ExcelFieldGroupDto> GetGroups() => Groups.Select(group => new ExcelFieldGroupDto
    {
        GroupCode = group.Code, DisplayName = group.Name
    }).ToArray();

    /// <summary>按不区分大小写的编码定位规范分组；未知分组返回空。</summary>
    public static string? ResolveGroup(string? groupCode) => Groups.FirstOrDefault(group =>
        string.Equals(group.Code, groupCode?.Trim(), StringComparison.OrdinalIgnoreCase)).Code;

    /// <summary>返回目标字段目录；未知分组返回空列表。</summary>
    public static IReadOnlyList<ExcelTargetFieldDto> GetFields(string? groupCode)
    {
        var group = Groups.FirstOrDefault(item => string.Equals(item.Code, groupCode, StringComparison.Ordinal));
        return group.Fields?.Select(field => new ExcelTargetFieldDto
        {
            FieldKey = field.Key, DisplayName = field.Name,
            SupportsCreate = field.Create, SupportsUpdate = field.Update
        }).ToArray() ?? [];
    }

    /// <summary>检查目标字段是否确实属于该分组的受控输入目录。</summary>
    public static bool ContainsField(string groupCode, string? fieldKey)
        => GetFields(groupCode).Any(field => string.Equals(field.FieldKey, fieldKey, StringComparison.Ordinal));
}
