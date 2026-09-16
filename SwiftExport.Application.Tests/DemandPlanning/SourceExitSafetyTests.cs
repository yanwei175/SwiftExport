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

/// <summary>验证物料领域只提供批量 NetDemand 历史证明，不再跨域解释采购、入库或交付事实。</summary>
[TestClass]
public sealed class SourceExitSafetyTests
{
    /// <summary>物料评估完整透传无历史、活动修订和安全历史三类结果。</summary>
    [TestMethod]
    public async Task MaterialAssessmentReturnsHistoryAndActivityFacts()
    {
        using var session = (DbSession)RuntimeHelpers.GetUninitializedObject(typeof(DbSession));
        session.Transaction = DispatchProxy.Create<IDbTransaction, NoOpPort>();
        var repository = DispatchProxy.Create<INetDemandRepository, ExitRepositoryPort>();
        var port = (ExitRepositoryPort)(object)repository;
        var service = new MaterialDemandEstablishmentService(
            new ServiceBaseContext(session, NullLoggerFactory.Instance), null!, repository,
            null!, null!, null!, null!, null!, null!, null!, null!, null!);
        var empty = Guid.NewGuid();
        var active = Guid.NewGuid();
        var safe = Guid.NewGuid();
        port.Assessments =
        [
            new() { GrossDemandId = empty, HistoryCount = 0, IsHistoryComplete = true },
            new() { GrossDemandId = active, HistoryCount = 2, HasActiveRevision = true, IsHistoryComplete = false },
            new() { GrossDemandId = safe, HistoryCount = 3, IsHistoryComplete = true }
        ];

        var result = await service.AssessSourceExitAsync([safe, empty, active]);

        Assert.AreEqual(0, result[empty].HistoryCount);
        Assert.IsTrue(result[active].HasActiveRevision);
        Assert.AreEqual(3, result[safe].HistoryCount);
        Assert.IsTrue(result[safe].IsHistoryComplete);
        Assert.AreEqual(1, port.CallCount);
    }

    /// <summary>只模拟仓储已知阻断结果，不提供虚构交付余额或连接生产数据库。</summary>
    public class ExitRepositoryPort : DispatchProxy
    {
        public IReadOnlyList<MaterialDemandSourceExitAssessment> Assessments { get; set; } = [];
        public int CallCount { get; private set; }
        /// <summary>仅接受退出核验调用，其他调用视为测试越界。</summary>
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method?.Name != nameof(INetDemandRepository.AssessSourceExitForUpdateAsync))
                throw new NotSupportedException(method?.Name);
            CallCount++;
            return Task.FromResult(Assessments);
        }
    }

    /// <summary>仅提供非空事务标识与无副作用释放，避免单元测试打开数据库。</summary>
    public class NoOpPort : DispatchProxy
    {
        /// <summary>模拟事务释放，不执行真实提交或回滚。</summary>
        protected override object? Invoke(MethodInfo? method, object?[]? args) => null;
    }
}
