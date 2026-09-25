using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Services.SalesOrders.Internal;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Tests.Orders;

/// <summary>核对销售头行仓储错误码到公开命令错误码的稳定映射。</summary>
[TestClass]
public sealed class SalesOrderRulesTests
{
    /// <summary>基础仓储的并发写入失败应作为版本冲突返回，批量行写入同样适用。</summary>
    [TestMethod]
    public void RepositoryConcurrencyConflictMapsToVersionConflict()
    {
        var failed = RepositoryWriteResult.Fail(Guid.NewGuid(), 3, "CONCURRENCY_CONFLICT", "版本已变化");
        var single = Assert.ThrowsExactly<SalesOrderValidationException>(
            () => SalesOrderRules.EnsureWriteSucceeded(failed));
        Assert.AreEqual(ServiceErrorCodes.VersionConflict, single.Code);

        var batch = Assert.ThrowsExactly<SalesOrderValidationException>(
            () => SalesOrderRules.EnsureAllWritesSucceeded([RepositoryWriteResult.Ok(Guid.NewGuid(), 1, 2), failed]));
        Assert.AreEqual(ServiceErrorCodes.VersionConflict, batch.Code);
    }
}
