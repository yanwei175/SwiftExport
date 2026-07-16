using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Services.DemandPlanning;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Entities.DemandPlanning;

namespace SwiftExport.Application.Tests.DemandPlanning;

[TestClass]
public sealed class NetDemandPlanCommandIdempotencyPolicyTests
{
    private readonly NetDemandPlanCommandIdempotencyPolicy _policy = new();

    [TestMethod]
    public void SuccessfulCommandRetry_ReturnsAlreadyAppliedWithStoredResult()
    {
        var allocationId = Guid.NewGuid();
        var newAllocationId = Guid.NewGuid();
        var affectedId = Guid.NewGuid();
        var fingerprint = _policy.BuildFingerprint("Replace", allocationId, 1,
            DemandExecutionTypeEnum.Purchase, Guid.NewGuid(), new DateTime(2026, 8, 10), "调整");
        var record = new NetDemandPlanCommandRecord
        {
            CommandId = Guid.NewGuid(),
            OriginalAllocationId = allocationId,
            CommandType = "Replace",
            RequestFingerprint = fingerprint,
            Outcome = NetDemandCommandOutcomeEnum.Succeeded,
            NewAllocationId = newAllocationId,
            NewAllocationVersion = 1,
            ResultMessage = "计划替代成功。",
            AffectedAllocationIds = affectedId.ToString("D")
        };

        var replay = _policy.CreateReplay(record, fingerprint, "row-1", 1);

        Assert.IsTrue(replay.Success);
        Assert.IsNotNull(replay.Result);
        Assert.AreEqual(NetDemandCommandOutcomeEnum.AlreadyApplied, replay.Result.Outcome);
        Assert.AreEqual(newAllocationId, replay.Result.NewAllocationId);
        CollectionAssert.AreEqual(new[] { affectedId }, replay.Result.AffectedCustomerSuppliedAllocationIds.ToArray());
    }

    [TestMethod]
    public void ReusedCommandIdWithDifferentPayload_IsRejected()
    {
        var allocationId = Guid.NewGuid();
        var stored = _policy.BuildFingerprint("Replace", allocationId, 1,
            DemandExecutionTypeEnum.Purchase, Guid.NewGuid(), null, "调整一");
        var incoming = _policy.BuildFingerprint("Replace", allocationId, 1,
            DemandExecutionTypeEnum.Purchase, Guid.NewGuid(), null, "调整二");
        var record = new NetDemandPlanCommandRecord
        {
            CommandId = Guid.NewGuid(),
            OriginalAllocationId = allocationId,
            CommandType = "Replace",
            RequestFingerprint = stored,
            Outcome = NetDemandCommandOutcomeEnum.Succeeded,
            NewAllocationId = Guid.NewGuid(),
            NewAllocationVersion = 1
        };

        var replay = _policy.CreateReplay(record, incoming, null, 0);

        Assert.IsFalse(replay.Success);
        Assert.AreEqual("IDEMPOTENCY_KEY_REUSED", replay.ErrorCode);
    }
}
