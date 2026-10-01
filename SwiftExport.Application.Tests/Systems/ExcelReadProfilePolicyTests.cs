using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Services.Systems.ExcelReadProfiles;
using SwiftExport.Contracts.DTOs.SystemDtos.ExcelReadProfiles;

namespace SwiftExport.Application.Tests.Systems;

/// <summary>验证地址安全、分步配置及共同读取边界，不依赖 Excel 或数据库。</summary>
[TestClass]
public sealed class ExcelReadProfilePolicyTests
{
    /// <summary>接受边界内的单格地址并规范化，拒绝范围、公式、外部引用和越界位置。</summary>
    [TestMethod]
    [DataRow("$a$8", true, "A8")]
    [DataRow("XFD1048576", true, "XFD1048576")]
    [DataRow("XFE1", false, "")]
    [DataRow("A1048577", false, "")]
    [DataRow("A0", false, "")]
    [DataRow("A01", false, "")]
    [DataRow("A1:B3", false, "")]
    [DataRow("Sheet1!A1", false, "")]
    [DataRow("[book.xlsx]Sheet1!A1", false, "")]
    [DataRow("=A1", false, "")]
    public void CellAddresses_AreBounded(string input, bool valid, string expected)
    {
        Assert.AreEqual(valid, ExcelReadProfilePolicy.TryCell(input, out var normalized, out _));
        if (valid) Assert.AreEqual(expected, normalized);
    }

    /// <summary>配置可以分步保存，但没有字段或向下读取没有结束规则时不能标记可读取。</summary>
    [TestMethod]
    public void IncompleteProfiles_AreSavableButCannotRead()
    {
        var update = Request();
        Assert.IsNull(ExcelReadProfilePolicy.ValidateUpdate(update));
        var profile = new ExcelReadProfileDto();
        ExcelReadProfilePolicy.Evaluate(profile);
        Assert.IsFalse(profile.CanRead);
        Assert.AreEqual("EXCEL_READ_FIELDS_REQUIRED", profile.Issues.Single().Code);
        update.Fields.Add(Field("Line.Sku", "订单", "A8", true));
        Assert.IsNull(ExcelReadProfilePolicy.ValidateUpdate(update));
        profile.Fields = update.Fields;
        ExcelReadProfilePolicy.Evaluate(profile);
        Assert.AreEqual("EXCEL_READ_END_REQUIRED", profile.Issues.Single().Code);
        profile.EndMode = 1;
        ExcelReadProfilePolicy.Evaluate(profile);
        Assert.AreEqual("EXCEL_READ_COLUMN_REQUIRED", profile.Issues.Single().Code);
        profile.LastRowColumn = "A";
        ExcelReadProfilePolicy.Evaluate(profile);
        Assert.IsTrue(profile.CanRead);
    }

    /// <summary>工作表名称仅作参考，向下读取只要求起始行一致，不要求参考名称相同。</summary>
    [TestMethod]
    public void DownwardFields_MustShareStartRowButIgnoreWorksheetName()
    {
        var profile = new ExcelReadProfileDto { Fields = [Field("Header.Code", "单头", "B3", false)] };
        ExcelReadProfilePolicy.Evaluate(profile);
        Assert.IsTrue(profile.CanRead);
        profile.EndMode = 1;
        profile.LastRowColumn = "A";
        profile.Fields.Add(Field("Line.Sku", "订单", "A8", true));
        profile.Fields.Add(Field("Line.Price", "明细", "D9", true));
        ExcelReadProfilePolicy.Evaluate(profile);
        Assert.AreEqual("EXCEL_READ_START_ROW_MISMATCH", profile.Issues.Single().Code);
        profile.Fields[2].CellAddress = "D8";
        ExcelReadProfilePolicy.Evaluate(profile);
        Assert.IsTrue(profile.CanRead);
    }

    /// <summary>名称可以省略或为空；完整的向下读取规则不会因缺少参考名称而被阻断。</summary>
    [TestMethod]
    public void WorksheetName_IsOptionalReferenceInformation()
    {
        var request = Request();
        request.EndMode = 1;
        request.LastRowColumn = "A";
        request.Fields = [Field("Line.Sku", null!, "A8", true), Field("Line.Quantity", "", "D8", true)];
        Assert.IsNull(ExcelReadProfilePolicy.ValidateUpdate(request));
        var profile = new ExcelReadProfileDto { EndMode = 1, LastRowColumn = "A", Fields = request.Fields };
        ExcelReadProfilePolicy.Evaluate(profile);
        Assert.IsTrue(profile.CanRead);
        Assert.HasCount(0, profile.Issues);
    }

    /// <summary>汇总行必须存在且位于明细起始行之后，汇总行本身不属于明细。</summary>
    [TestMethod]
    public void SummaryBoundary_MustFollowStartRow()
    {
        var profile = new ExcelReadProfileDto { EndMode = 2, Fields = [Field("Line.Sku", "订单", "A8", true)] };
        ExcelReadProfilePolicy.Evaluate(profile);
        Assert.AreEqual("EXCEL_READ_SUMMARY_REQUIRED", profile.Issues.Single().Code);
        profile.SummaryCellAddress = "D8";
        ExcelReadProfilePolicy.Evaluate(profile);
        Assert.AreEqual("EXCEL_READ_SUMMARY_BEFORE_START", profile.Issues.Single().Code);
        profile.SummaryCellAddress = "D30";
        ExcelReadProfilePolicy.Evaluate(profile);
        Assert.IsTrue(profile.CanRead);
    }

    /// <summary>拒绝丢失完整字段快照、重复键、错误工作表和与结束方式不匹配的边界值。</summary>
    [TestMethod]
    public void InvalidSnapshots_AreRejected()
    {
        var request = Request();
        request.Fields = null!;
        Assert.AreEqual("Fields", ExcelReadProfilePolicy.ValidateUpdate(request)!.Field);
        request = Request();
        request.Fields = [Field("Line.Sku", "订单", "A8", true), Field("line.sku", "订单", "B8", true)];
        Assert.AreEqual("Fields[1].TargetFieldKey", ExcelReadProfilePolicy.ValidateUpdate(request)!.Field);
        request.Fields.RemoveAt(1);
        request.Fields[0].WorksheetName = "bad/sheet";
        Assert.AreEqual("Fields[0].WorksheetName", ExcelReadProfilePolicy.ValidateUpdate(request)!.Field);
        request.Fields[0].WorksheetName = "订单";
        request.LastRowColumn = "A";
        Assert.AreEqual("LastRowColumn", ExcelReadProfilePolicy.ValidateUpdate(request)!.Field);
        request.EndMode = 1;
        request.LastRowColumn = "XFE";
        Assert.AreEqual("LastRowColumn", ExcelReadProfilePolicy.ValidateUpdate(request)!.Field);
    }

    private static UpdateExcelReadProfileRequest Request() => new() { ExpectedVersion = 1, ConfigName = "客户 A", Fields = [] };
    private static ExcelReadFieldDto Field(string key, string sheet, string cell, bool downward) =>
        new() { TargetFieldKey = key, WorksheetName = sheet, CellAddress = cell, IsDownward = downward };
}
