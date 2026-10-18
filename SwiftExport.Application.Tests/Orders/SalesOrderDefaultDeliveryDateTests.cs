using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.SystemDtos.SystemParameters;

namespace SwiftExport.Application.Tests.Orders;

/// <summary>通过真实保存用例和命令执行器验证默认交期快照，不连接数据库。</summary>
[TestClass]
public sealed class SalesOrderDefaultDeliveryDateTests
{
    /// <summary>初始六十天、自定义和零天都按订单日期自然日计算，行空交期保持继承语义。</summary>
    [TestMethod]
    [DataRow(null, 60)] [DataRow("12", 12)] [DataRow("0", 0)]
    public async Task Create_AppliesDefaultOnlyToEntity(string? configured, int days)
    {
        var f = new 销售草稿发票号唯一测试.Fixture();
        if (configured is not null) f.Parameters.OtherParameters[SystemParameterKeys.SalesDefaultDeliveryDays] = (configured, 1);
        var request = f.Request(false); request.Header.OrderDate = new DateTime(2026, 10, 18, 18, 30, 0);
        var original = JsonSerializer.Serialize(request);
        Assert.IsTrue((await f.Service.SaveAsync(request)).Success);
        Assert.AreEqual(new DateTime(2026, 10, 18).AddDays(days), f.LastWrittenHeader!.DeliveryDate);
        Assert.AreEqual(original, JsonSerializer.Serialize(request), "执行不得修改命令指纹输入。");
        Assert.IsNull(request.Lines.Single().ExpectedDeliveryDate);
    }

    /// <summary>用户明确日期不读取默认参数；更新历史空日期也不补齐。</summary>
    [TestMethod]
    [DataRow(false, true)] [DataRow(true, false)] [DataRow(true, true)]
    public async Task ExplicitOrExistingDate_DoesNotReadDefaults(bool update, bool explicitDate)
    {
        var f = new 销售草稿发票号唯一测试.Fixture();
        f.Parameters.OtherParameters[SystemParameterKeys.SalesDefaultDeliveryDays] = ("invalid", 1);
        var request = f.Request(update); request.Header.DeliveryDate = explicitDate ? new DateTime(2027, 1, 2) : null;
        Assert.IsTrue((await f.Service.SaveAsync(request)).Success);
        Assert.AreEqual(request.Header.DeliveryDate, f.LastWrittenHeader!.DeliveryDate);
        Assert.AreEqual(0, f.Parameters.Reads);
    }

    /// <summary>非法持久参数或日期溢出明确失败，不生成头行或成功回执。</summary>
    [TestMethod]
    [DataRow("bad")] [DataRow("-1")] [DataRow("1.5")] [DataRow("2147483648")] [DataRow("2147483647")]
    public async Task InvalidPersistedOrOverflow_IsRejected(string value)
    {
        var f = new 销售草稿发票号唯一测试.Fixture();
        f.Parameters.OtherParameters[SystemParameterKeys.SalesDefaultDeliveryDays] = (value, 1);
        var request = f.Request(false); request.Header.OrderDate = new DateTime(9999, 12, 31);
        var result = await f.Service.SaveAsync(request);
        Assert.IsFalse(result.Success); Assert.IsNull(request.Header.DeliveryDate);
        Assert.AreEqual(0, f.HeaderWrites + f.LineWrites + f.ReceiptWrites);
    }

    /// <summary>相同CommandId重放首次日期，参数变化仅影响下一张新订单。</summary>
    [TestMethod]
    public async Task Replay_KeepsOriginalDateWithoutReadingChangedParameter()
    {
        var f = new 销售草稿发票号唯一测试.Fixture(); var request = f.Request(false);
        request.Header.OrderDate = new DateTime(2026, 10, 18);
        var first = await f.Service.SaveAsync(request); Assert.IsTrue(first.Success);
        Assert.AreEqual(new DateTime(2026, 12, 17), first.Data.Document.Header.DeliveryDate);
        var reads = f.Parameters.Reads;
        f.Parameters.OtherParameters[SystemParameterKeys.SalesDefaultDeliveryDays] = ("3", 1);
        var replay = await f.Service.SaveAsync(request); Assert.IsTrue(replay.Success);
        Assert.AreEqual(first.Data.Document.Header.DeliveryDate, replay.Data.Document.Header.DeliveryDate);
        Assert.AreEqual(reads, f.Parameters.Reads); Assert.AreEqual(1, f.HeaderWrites);
        var next = f.Request(false); next.Header.OrderDate = request.Header.OrderDate;
        Assert.IsTrue((await f.Service.SaveAsync(next)).Success);
        Assert.AreEqual(new DateTime(2026, 10, 21), f.LastWrittenHeader!.DeliveryDate);
    }
}
