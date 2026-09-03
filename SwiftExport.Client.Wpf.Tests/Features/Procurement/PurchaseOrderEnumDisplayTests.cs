using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderManagerDto;
using SwiftExport.Contracts.SysEnums;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;
using SwiftExportClient.Resources.Converters;
using System.Globalization;
using System.Text.Json;

namespace SwiftExport.Client.Wpf.Tests.Features.Procurement;

/// <summary>验证采购页面从传输到展示始终保留真实枚举，中文文本只在显示边界产生。</summary>
[TestClass]
public sealed class PurchaseOrderEnumDisplayTests
{
    [TestMethod]
    public void NumericJsonStatusesMustDeserializeToClrEnums()
    {
        var dto = JsonSerializer.Deserialize<PurchaseOrderManagerListItemDto>(
            "{\"ConfirmationStatus\":1,\"LifecycleStatus\":8}");

        Assert.IsNotNull(dto);
        Assert.AreEqual(ConfirmationStatusEnum.已确认, dto.ConfirmationStatus);
        Assert.AreEqual(PurchaseOrderLifecycleStatusEnum.Closed, dto.LifecycleStatus);
    }

    [TestMethod]
    public void EnumDescriptionConverterMustReturnProcurementChineseDescriptions()
    {
        var converter = new EnumDescriptionConverter();
        var cases = new (Enum Value, string Text)[]
        {
            (PurchaseOrderLifecycleStatusEnum.Active, "活动"),
            (PurchaseOrderLifecycleStatusEnum.Closed, "已关闭"),
            (PurchaseOrderLifecycleStatusEnum.Voided, "已作废"),
            (ConfirmationStatusEnum.待确认, "待确认"),
            (ConfirmationStatusEnum.已确认, "已确认")
        };

        foreach (var item in cases)
            Assert.AreEqual(item.Text, converter.Convert(item.Value, typeof(string), null, CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public void FilterOptionMustKeepChineseDisplaySeparateFromRequestValue()
    {
        var option = new PurchaseOrderFilterOption<PurchaseOrderLifecycleStatusEnum>(
            PurchaseOrderLifecycleStatusEnum.Voided, "已作废");

        Assert.AreEqual("已作废", option.DisplayText);
        Assert.AreEqual(PurchaseOrderLifecycleStatusEnum.Voided, option.Value);
    }
}
