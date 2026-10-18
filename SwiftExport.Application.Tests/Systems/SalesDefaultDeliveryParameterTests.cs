using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Tests.Files;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.SystemDtos.SystemParameters;

namespace SwiftExport.Application.Tests.Systems;

/// <summary>复用系统参数服务夹具验证销售天数定义、管理员整数维护及非法持久值修复。</summary>
public sealed partial class SystemParameterServiceTests
{
    /// <summary>未配置时公开六十天建议值，不写数据库；非法旧值仍可读取供管理员修复。</summary>
    [TestMethod]
    public async Task SalesDeliveryDays_ListKeepsDefaultAndRepairableRawValue()
    {
        var repository = new SystemParameterRepositoryStub(null); var service = Create(repository, new Session());
        var list = await service.ListAsync(); Assert.IsTrue(list.Success);
        var value = list.Data.Single(item => item.Key == SystemParameterKeys.SalesDefaultDeliveryDays);
        Assert.AreEqual("60", value.Value); Assert.AreEqual(0, value.Version); Assert.IsFalse(value.IsConfigured);
        repository.OtherParameters[SystemParameterKeys.SalesDefaultDeliveryDays] = ("bad", 2);
        list = await service.ListAsync(); Assert.IsTrue(list.Success);
        Assert.AreEqual("bad", list.Data.Single(item => item.Key == SystemParameterKeys.SalesDefaultDeliveryDays).Value);
        Assert.AreEqual(0, repository.Writes);
    }

    /// <summary>整数规则支持零、规范空白/前导零，拒绝负数、小数、溢出和URL；保留版本条件保存。</summary>
    [TestMethod]
    [DataRow("0", true, "0")] [DataRow(" 012 ", true, "12")]
    [DataRow("-1", false, "")] [DataRow("1.5", false, "")]
    [DataRow("2147483648", false, "")] [DataRow("https://example.test", false, "")]
    public async Task SalesDeliveryDays_UpdateRequiresNonNegativeInteger(string input, bool expected, string normalized)
    {
        var repository = new SystemParameterRepositoryStub(null); var session = new Session();
        var service = Create(repository, session);
        var result = await service.UpdateAsync(SystemParameterKeys.SalesDefaultDeliveryDays, Request(0, input));
        Assert.AreEqual(expected, result.Success);
        if (expected) { Assert.AreEqual(normalized, result.Data.Value); Assert.AreEqual(1, result.Data.Version); }
        else { Assert.AreEqual(ServiceErrorCodes.Validation, result.Error.Code); Assert.AreEqual(0, repository.Writes + session.Db.Begins); }
    }
}
