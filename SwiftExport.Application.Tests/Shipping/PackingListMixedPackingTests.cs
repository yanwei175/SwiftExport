using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Services.Shipping;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;

namespace SwiftExport.Application.Tests.Shipping;

/// <summary>拼箱保存契约行为测试，不连接数据库。</summary>
[TestClass]
public sealed class PackingListMixedPackingTests
{
    private static readonly MethodInfo Validate = typeof(SwiftExport.Application.Services.Shipping.PackingLists.PackingListDraftService).GetMethod("ValidateLineInputs", BindingFlags.Static | BindingFlags.NonPublic)!;
    private static PackingListDraftLineInput Row(int n, Guid? group = null) => new()
    { Uid = Guid.NewGuid(), GrossDemandId = Guid.NewGuid(), LineNumber = n, Qty = 3, PackingGroupId = group };
    private static void Check(params PackingListDraftLineInput[] rows) => Validate.Invoke(null, [rows, Array.Empty<PackingListDeletedLineInput>()]);

    /// <summary>保存端把拼箱成员规范化为整行数量一箱，不采信客户端每箱量。</summary>
    [TestMethod]
    public void MixedRowsNormalizeButOrdinaryRemainsUnknown()
    {
        var id = Guid.NewGuid(); var a = Row(1, id); var b = Row(2, id); var ordinary = Row(3);
        a.PcsPerPackage = 999;
        Check(a, b, ordinary);
        Assert.AreEqual(3m, a.PcsPerPackage); Assert.AreEqual(3m, b.PcsPerPackage);
        Assert.IsNull(ordinary.PcsPerPackage);
    }

    /// <summary>空标识、组内冲突和不连续成员均不能保存。</summary>
    [TestMethod]
    public void InvalidGroupFactsAreRejected()
    {
        Assert.ThrowsExactly<TargetInvocationException>(() => Check(Row(1, Guid.Empty)));
        var id = Guid.NewGuid(); var a = Row(1, id); var b = Row(3, id);
        Assert.ThrowsExactly<TargetInvocationException>(() => Check(a, Row(2), b));
        b.LineNumber = 2; a.NwPerPackage = 1; b.NwPerPackage = 2;
        Assert.ThrowsExactly<TargetInvocationException>(() => Check(a, b));
        b.NwPerPackage = 1; a.Marks = "A"; b.Marks = "B";
        Assert.ThrowsExactly<TargetInvocationException>(() => Check(a, b));
    }

    /// <summary>单成员组合法，净重不能超过毛重。</summary>
    [TestMethod]
    public void SingleMemberAndUnknownAreAllowedButOverweightIsNot()
    {
        var row = Row(1, Guid.NewGuid()); Check(row);
        row.NwPerPackage = 2; row.GwPerPackage = 1;
        Assert.ThrowsExactly<TargetInvocationException>(() => Check(row));
    }

    /// <summary>只改变包装归属也必须改变保存幂等指纹。</summary>
    [TestMethod]
    public void GroupIdentityChangesFingerprint()
    {
        var fingerprint = typeof(SwiftExport.Application.Services.Shipping.PackingLists.PackingListDraftService).GetMethod("Fingerprint", BindingFlags.Static | BindingFlags.NonPublic)!;
        var row = Row(1);
        object?[] args = ["Update", Guid.NewGuid(), 1, null, new[] { row }, Array.Empty<PackingListDeletedLineInput>()];
        var old = fingerprint.Invoke(null, args);
        row.PackingGroupId = Guid.NewGuid();
        Assert.AreNotEqual(old, fingerprint.Invoke(null, args));
    }
}
