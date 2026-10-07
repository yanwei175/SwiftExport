using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Services.Customers.Management;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Customers.Management;
using SwiftExport.Core.Interfaces.Data;

namespace SwiftExport.Application.Tests.Customers;

/// <summary>用真实客户服务与条件写入夹具验证通用更新的外层身份、版本契约。</summary>
[TestClass]
public sealed class CustomerUpdateConcurrencyTests
{
    /// <summary>过期外层版本不能被 DTO 版本绕过，DTO 身份也不能改变更新目标。</summary>
    [TestMethod]
    [DataRow(7, 8, false)]
    [DataRow(8, 7, false)]
    [DataRow(8, 0, false)]
    [DataRow(8, 8, true)]
    [DataRow(8, 8, false)]
    public async Task Update_UsesOuterIdentityAndVersion(int outerVersion, int dataVersion, bool foreignDataId)
    {
        var targetId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var rows = new Dictionary<Guid, Customer>
        {
            [targetId] = new() { Uid = targetId, Version = 8, DocRelateNo = "C1", CustomerCode = "C1", CustomerName = "原客户" },
            [otherId] = new() { Uid = otherId, Version = 8, DocRelateNo = "C2", CustomerCode = "C2", CustomerName = "其他客户" }
        };
        var writes = 0;
        Guid? attemptedId = null;
        int? attemptedVersion = null;
        var repository = DispatchProxy.Create<ICustomerRepository, RepositoryProbe>();
        ((RepositoryProbe)(object)repository).Handler = (method, args) =>
        {
            switch (method.Name)
            {
                case "GetExistingIdListAsync":
                    return Task.FromResult<IReadOnlyList<Guid>>(((IEnumerable<Guid>)args![0]!).Where(rows.ContainsKey).ToArray());
                case "GetCustomerCodesUsedByOtherCustomersAsync":
                    return Task.FromResult(new List<string>());
                case "GetByIdAsync":
                    Assert.AreEqual(targetId, args![0]);
                    var stored = rows[(Guid)args[0]!];
                    // 读取副本，失败写入不能通过共享对象修改持久化状态。
                    return Task.FromResult<Customer?>(new Customer { Uid = stored.Uid, Version = stored.Version,
                        DocRelateNo = stored.DocRelateNo, CustomerCode = stored.CustomerCode, CustomerName = stored.CustomerName });
                case "UpdateAsync":
                    var candidate = (Customer)args![0]!;
                    attemptedId = candidate.Uid;
                    attemptedVersion = candidate.Version;
                    // 模拟仓储 WHERE uid/version 条件，只在条件成立时提交数据。
                    if (!rows.TryGetValue(candidate.Uid, out var row) || row.Version != candidate.Version)
                        return Task.FromResult(RepositoryWriteResult.Fail(candidate.Uid, candidate.Version,
                            "CONCURRENCY_CONFLICT", "版本已变化"));
                    writes++;
                    row.CustomerName = candidate.CustomerName;
                    row.Version++;
                    return Task.FromResult(RepositoryWriteResult.Ok(row.Uid, candidate.Version, row.Version));
                default:
                    throw new NotSupportedException(method.Name);
            }
        };
        var session = DispatchProxy.Create<IDbSession, RepositoryProbe>();
        ((RepositoryProbe)(object)session).Handler = (method, _) => throw new NotSupportedException(method.Name);
        var service = new CustomerService(repository, null!, new ServiceBaseContext(session, NullLoggerFactory.Instance));
        var dataId = foreignDataId ? otherId : targetId;
        var request = new UpdateItemRequest<CustomerDto> { Uid = targetId, Version = outerVersion,
            ClientKey = "customer-row", RowIndex = 4,
            Data = new() { Uid = dataId, Version = dataVersion, CustomerCode = "C1", CustomerName = "更新客户" } };
        var result = await service.UpdateAsync(request);
        Assert.IsTrue(result.Success, result.Message); // 保持现有逐行保存结果契约。
        Assert.AreEqual(targetId, attemptedId);
        Assert.AreEqual(outerVersion, attemptedVersion);
        Assert.AreEqual("customer-row", result.Data.ClientKey);
        Assert.AreEqual(4, result.Data.RowIndex);
        Assert.AreEqual(SaveOperation.Update, result.Data.Operation);
        Assert.AreEqual(targetId, result.Data.Uid);
        Assert.AreEqual(dataId, request.Data.Uid, "映射保护不修改调用方 DTO。");
        Assert.AreEqual(dataVersion, request.Data.Version);
        Assert.AreEqual("其他客户", rows[otherId].CustomerName);
        Assert.AreEqual(8, rows[otherId].Version);
        if (outerVersion == 7)
        {
            Assert.IsFalse(result.Data.Success);
            Assert.AreEqual("CONCURRENCY_CONFLICT", result.Data.ErrorCode);
            Assert.AreEqual(0, writes);
            Assert.AreEqual("原客户", rows[targetId].CustomerName);
            Assert.AreEqual(8, rows[targetId].Version);
        }
        else
        {
            Assert.IsTrue(result.Data.Success);
            Assert.AreEqual(1, writes);
            Assert.AreEqual("更新客户", rows[targetId].CustomerName);
            Assert.AreEqual(8, result.Data.OldVersion);
            Assert.AreEqual(9, result.Data.NewVersion);
            Assert.AreEqual(9, rows[targetId].Version);
        }
    }

    /// <summary>仅代理本测试允许的仓储调用，会话不允许访问连接。</summary>
    public class RepositoryProbe : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object> Handler { get; set; } = null!;
        /// <summary>执行夹具的条件写入模型，不调用真实数据库。</summary>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!, args);
    }
}
