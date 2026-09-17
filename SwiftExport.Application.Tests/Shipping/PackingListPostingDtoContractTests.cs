using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;

namespace SwiftExport.Application.Tests.Shipping;

/// <summary>约束过账命令只传确认信息，不允许客户端覆盖库存分配和操作人。</summary>
[TestClass]
public sealed class PackingListPostingDtoContractTests
{
    /// <summary>命令字段采用白名单，新增服务端权威字段时本测试必须失败。</summary>
    [TestMethod]
    public void CommandsExcludeServerAuthority()
    {
        CollectionAssert.AreEquivalent(new[] { "CommandId", "ExpectedVersion", "BusinessDate", "PreviewToken" },
            typeof(PostPackingListCommand).GetProperties().Select(x => x.Name).ToArray());
        CollectionAssert.AreEquivalent(new[] { "CommandId", "ExpectedVersion", "BusinessDate", "Reason", "PreviewToken" },
            typeof(ReversePackingListCommand).GetProperties().Select(x => x.Name).ToArray());
    }

    /// <summary>预览的图片、来源身份、空单位与四位数量能够完整序列化。</summary>
    [TestMethod]
    public void PreviewPreservesDisplayAndNullableFacts()
    {
        var source = Guid.NewGuid();
        var dto = new PackingListPostingPreviewDto
        {
            Actions = [new() { ActionKind = "Delivery", GrossDemandId = source,
                Quantity = 1.2345m, ProductId = null, Unit = null, ImagePath = "images/sample.png" }],
            Blockers = [new() { Code = "SOURCE_INVALID", GrossDemandId = source, ShortfallQty = null }]
        };
        var copy = JsonSerializer.Deserialize<PackingListPostingPreviewDto>(JsonSerializer.Serialize(dto))!;
        Assert.AreEqual(source, copy.Actions[0].GrossDemandId);
        Assert.AreEqual(1.2345m, copy.Actions[0].Quantity);
        Assert.AreEqual("images/sample.png", copy.Actions[0].ImagePath);
        Assert.IsNull(copy.Actions[0].ProductId);
        Assert.IsNull(copy.Actions[0].Unit);
        Assert.IsNull(copy.Blockers[0].ShortfallQty);
        Assert.IsNull(copy.PreviewToken);
    }
}
