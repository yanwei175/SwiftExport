using System.Data;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.DemandPlanning;
using SwiftExport.Application.Services.DemandPlanning;
using SwiftExport.Core.Interfaces.DemandPlanning;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Tests.DemandPlanning;

/// <summary>验证来源退出不能将无已知供货阻断误认为无交付事实。</summary>
[TestClass]
public sealed class SourceExitSafetyTests
{
    /// <summary>已知下游事实返回阻断；查询为空时明确报告安全性未知，而不是放行。</summary>
    [TestMethod]
    public async Task KnownBlockerAndUnknownDeliveryBothPreventExit()
    {
        using var session = (DbSession)RuntimeHelpers.GetUninitializedObject(typeof(DbSession));
        session.Transaction = DispatchProxy.Create<IDbTransaction, NoOpPort>();
        var repository = DispatchProxy.Create<INetDemandRepository, ExitRepositoryPort>();
        var port = (ExitRepositoryPort)(object)repository;
        var service = new MaterialDemandEstablishmentService(
            new ServiceBaseContext(session, NullLoggerFactory.Instance), null!, repository,
            null!, null!, null!, null!, null!, null!, null!, null!, null!);
        var sourceId = Guid.NewGuid();
        var lineId = Guid.NewGuid();

        port.HasBlocker = true;
        Assert.IsTrue(await service.HasSourceExitBlockerAsync("SO", sourceId, [lineId]));
        port.HasBlocker = false;
        foreach (var type in new[] { "SO", "CR" })
        {
            var error = await Assert.ThrowsAsync<SourceDemandReceptionException>(
                () => service.HasSourceExitBlockerAsync(type, sourceId, [lineId]));
            Assert.AreEqual("SOURCE_EXIT_SAFETY_UNAVAILABLE", error.Code);
        }
        Assert.AreEqual(3, port.CallCount);
    }

    /// <summary>只模拟仓储已知阻断结果，不提供虚构交付余额或连接生产数据库。</summary>
    public class ExitRepositoryPort : DispatchProxy
    {
        public bool HasBlocker { get; set; }
        public int CallCount { get; private set; }
        /// <summary>仅接受退出核验调用，其他调用视为测试越界。</summary>
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method?.Name != nameof(INetDemandRepository.HasSourceExitBlockerAsync))
                throw new NotSupportedException(method?.Name);
            CallCount++;
            return Task.FromResult(HasBlocker);
        }
    }

    /// <summary>仅提供非空事务标识与无副作用释放，避免单元测试打开数据库。</summary>
    public class NoOpPort : DispatchProxy
    {
        /// <summary>模拟事务释放，不执行真实提交或回滚。</summary>
        protected override object? Invoke(MethodInfo? method, object?[]? args) => null;
    }
}
