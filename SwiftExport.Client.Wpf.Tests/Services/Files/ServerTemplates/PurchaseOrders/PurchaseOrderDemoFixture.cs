using System.IO;
using System.Reflection;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExportClient.Services.Files.ServerTemplates;
using SwiftExportClient.Services.Files.Templates;

namespace SwiftExport.Client.Wpf.Tests.Services.Files.ServerTemplates.PurchaseOrders;

/// <summary>提供采购单模板的虚构展示数据与受控资源；不读取业务 API、VSTO、数据库或页面编辑模型。</summary>
internal static class PurchaseOrderDemoFixture
{
    internal const string TemplateId = "purchase-order-vsto-demo";
    internal const string ContractKey = "purchase-order.vsto-demo";
    internal const string ResourceName = "PurchaseOrderVstoDemo.Template.xlsx";
    internal const string Title = "诗亦霖卫浴订单明细表";
    internal const string NotesText = "整单优惠：￥10.00（虚构演示，仅展示手工输入，不重新计算明细金额）。\n"
        + "优惠备注：示例说明由调用方提供，不根据价格倍率推导优惠。\n"
        + "采购备注：型号、数量、单价与金额均为演示快照，请按双方确认的正式订单执行。";
    internal static ExportTemplateContract Contract { get; } =
        new(DocumentTypeStaticsEnum.PurchaseOrder, ContractKey, 1);

    /// <summary>每次返回嵌入模板的独立副本，测试宿主按真实清单与 HTTP 内容管线发布。</summary>
    internal static async Task<byte[]> TemplateBytesAsync()
    {
        using var source = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("采购单示例模板未作为受控资源嵌入测试程序集。");
        using var result = new MemoryStream();
        await source.CopyToAsync(result);
        return result.ToArray();
    }

    /// <summary>提供旧模板既有的命名变量，单头字段使用明确虚构值，明细和说明均提前物化。</summary>
    internal static ExcelTemplateData Data(int count, bool allAmountsNull = false, string? boNo = "DEMO-BO-0001")
    {
        var lines = Lines(count, allAmountsNull);
        return new ExcelTemplateData(new Dictionary<string, object?>
        {
            ["Title"] = Title,
            ["Order"] = new PurchaseOrderDemoOrder(
                "DEMO-C001", "DEMO-PO-001", boNo, "PO-DEMO-0001", "演示业务员",
                new DateTime(2026, 10, 12), new DateTime(2026, 11, 15), new DateTime(2026, 11, 12), "演示跟单员",
                "虚构演示供应商", "演示进水管", "演示起泡器", "演示托盘", "演示内盒", "演示标贴",
                "演示说明书", "演示外箱", "演示商标", "特殊要求为示例文字；不包含真实业务数据。"),
            ["Lines"] = lines,
            ["Summary"] = new PurchaseOrderDemoSummary(lines.Length, "合计", NotesText),
        });
    }

    /// <summary>建立明确物化的明细数组；首行金额故意与数量乘单价不同，验证生成不会重新定价。</summary>
    internal static PurchaseOrderDemoLine[] Lines(int count, bool allAmountsNull = false)
    {
        if (count is < 0 or > 20) throw new ArgumentOutOfRangeException(nameof(count));
        var rows = new[]
        {
            new PurchaseOrderDemoLine("000123", "DEMO-001", "SUP-01", "演示混水龙头\n不含业务数据", "镀铬", "演示配置甲", 12.50m, 3m, 99.99m, "首行金额保留快照"),
            new PurchaseOrderDemoLine("000124", "DEMO-002", "SUP-02", "演示厨房龙头", "拉丝", "演示配置乙", 7.25m, 4m, 29.00m, "+文字备注保持原样"),
            new PurchaseOrderDemoLine("000125", "DEMO-003", "SUP-03", "演示配件", "黑色", "演示配置丙", 8.00m, 2m, 16.00m, "第三行"),
        };
        // 较长明细重复虚构快照，只验证排版展开；不构造额外产品或采购业务规则。
        return Enumerable.Range(0, count).Select(index => rows[index % rows.Length])
            .Select(line => allAmountsNull ? line with { UnitPrice = null, UnitAmount = null } : line).ToArray();
    }

    /// <summary>保存旧单头十八个显示槽所需十九个普通属性；BoNo 为空时模板显示 OrderNo。</summary>
    internal sealed record PurchaseOrderDemoOrder(
        string CustomerCode, string CustomerPoNo, string? BoNo, string OrderNo, string SalesPerson,
        DateTime? OrderDate, DateTime? ExpectedDate, DateTime? ReplyDate, string PurchasePerson,
        string SupplierName, string InletHoseType, string AeratorType, string PalletType,
        string BoxType, string LabelType, string InstructionManualType, string CartonType, string LogoType, string Remark);

    /// <summary>保存既有明细的十个展示字段，全部作为普通数据快照提供给 Report。</summary>
    internal sealed record PurchaseOrderDemoLine(
        string CustomerSku, string LinkSku, string SupplierSku, string DescriptionZh, string FinishZh, string FeaturesZh,
        decimal? UnitPrice, decimal Quantity, decimal? UnitAmount, string SubNote);

    /// <summary>提供行数、数量标签及合并后的说明文本，不承担重新定价或整单折扣计算。</summary>
    internal sealed record PurchaseOrderDemoSummary(int LineCount, string QuantityLabel, string NotesText);
}
