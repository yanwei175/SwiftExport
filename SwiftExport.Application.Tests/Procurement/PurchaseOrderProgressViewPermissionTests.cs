using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Models.Auth.Permissions.Evaluation;
using SwiftExport.Application.Services.Procurement.PurchaseOrders.Progress;
using SwiftExport.Application.Services.Procurement.PurchaseOrders.Support;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderProgress;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Tests.Procurement;

/// <summary>验证采购跟进读取使用独立 View 键，不要求采购订单 Read。</summary>
[TestClass]
public sealed class PurchaseOrderProgressViewPermissionTests
{
    /// <summary>仅有跟进 View 可进入业务参数校验，仅有订单 Read 在授权处拒绝。</summary>
    [TestMethod]
    public async Task Detail_UsesProgressViewBeforeQuery()
    {
        var session = (DbSession)RuntimeHelpers.GetUninitializedObject(typeof(DbSession));
        var permission = new PermissionStub();
        var reads = new ProcurementOrderReadSupport(new ServiceBaseContext(session, NullLoggerFactory.Instance),
            permission, new UserStub());
        var service = new PurchaseOrderProgressQueryService(reads, null!);

        permission.Allowed = [PermissionKeys.ProcurementOrderRead];
        var denied = await service.GetDetailAsync(Guid.Empty);
        Assert.AreEqual(ServiceErrorCodes.Forbidden, denied.Error.Code);

        permission.Allowed = [PermissionKeys.ProcurementProgressView];
        var reachedValidation = await service.GetDetailAsync(Guid.Empty);
        Assert.AreEqual(ServiceErrorCodes.Validation, reachedValidation.Error.Code);
        CollectionAssert.AreEqual(new[] { PermissionKeys.ProcurementProgressView,
            PermissionKeys.ProcurementProgressView }, permission.Keys);
    }

    /// <summary>Progress.View单独允许按ID直达，返回当前头版本；不先搜索单号，也不要求跟踪权限。</summary>
    [TestMethod]
    public async Task DirectDetail_ReadsCurrentHeaderWithProgressViewOnly()
    {
        var session=(DbSession)RuntimeHelpers.GetUninitializedObject(typeof(DbSession));var permission=new PermissionStub{Allowed=[PermissionKeys.ProcurementProgressView]};
        var queries=new QueryStub();var service=new PurchaseOrderProgressQueryService(new(new ServiceBaseContext(session,NullLoggerFactory.Instance),permission,new UserStub()),queries);
        var id=Guid.NewGuid();var result=await service.GetDetailAsync(id);
        Assert.IsTrue(result.Success);Assert.AreEqual(id,result.Data.PurchaseOrderId);Assert.AreEqual(18,result.Data.Version);Assert.IsFalse(result.Data.CanReportProgress);
        Assert.AreEqual(id,queries.LastId);Assert.AreEqual(1,queries.DetailReads);Assert.AreEqual(0,queries.SearchReads);
        CollectionAssert.AreEqual(new[]{PermissionKeys.ProcurementProgressView},permission.Keys);
    }

    /// <summary>保留的订单筛选在进入仓储前校验；正常请求无需提供已移除的反馈维度。</summary>
    [TestMethod]
    public async Task Search_ValidatesOrderFiltersBeforeRepository()
    {
        var session=(DbSession)RuntimeHelpers.GetUninitializedObject(typeof(DbSession));var permission=new PermissionStub{Allowed=[PermissionKeys.ProcurementProgressView]};
        var queries=new QueryStub();var service=new PurchaseOrderProgressQueryService(new(new ServiceBaseContext(session,NullLoggerFactory.Instance),permission,new UserStub()),queries);
        foreach(var request in new PurchaseOrderProgressSearchRequest[]{new(){PageIndex=0},new(){PageSize=201},new(){ConfirmationStatuses=null!},
            new(){LifecycleStatuses=[(SwiftExport.Contracts.SysEnums.PurchaseOrderLifecycleStatusEnum)77]},new(){Keyword=new string('x',101)},
            new(){PlannedDeliveryDateFrom=new(2026,10,21),PlannedDeliveryDateTo=new(2026,10,20)}})
            Assert.AreEqual(ServiceErrorCodes.Validation,(await service.SearchAsync(request)).Error.Code);
        Assert.AreEqual(0,queries.SearchReads);Assert.IsTrue((await service.SearchAsync(new())).Success);Assert.AreEqual(1,queries.SearchReads);
        Assert.IsTrue(permission.Keys.All(key=>key==PermissionKeys.ProcurementProgressView));
    }

    /// <summary>只记录按ID读取和选单次数，模拟本次服务端头版本，不提供数据库或写入通道。</summary>
    private sealed class QueryStub:IPurchaseOrderProgressQueryRepository
    {
        public int DetailReads,SearchReads;public Guid LastId;
        /// <summary>按传入身份返回当前模拟头版本，并记录详情读取次数。</summary>
        public Task<PurchaseOrderProgressDetailDto?> GetDetailAsync(Guid id,CancellationToken cancellationToken=default)
        {DetailReads++;LastId=id;return Task.FromResult<PurchaseOrderProgressDetailDto?>(new(){PurchaseOrderId=id,Version=18,CanReportProgress=false,ReadOnlyReason="订单已关闭"});}
        /// <summary>记录选单调用次数，以核实直达路径没有预先搜索订单。</summary>
        public Task<PagedResponseBase<PurchaseOrderProgressListItemDto>> SearchAsync(PurchaseOrderProgressSearchRequest request,CancellationToken cancellationToken=default)
        {SearchReads++;return Task.FromResult(new PagedResponseBase<PurchaseOrderProgressListItemDto>());}
    }

    private sealed class UserStub : ICurrentUser
    {
        public Guid? UserId { get; } = Guid.NewGuid();
        public string UserName => "跟进查看测试";
        public bool IsAuthenticated => true;
    }

    private sealed class PermissionStub : IPermissionEvaluationService
    {
        public IReadOnlyCollection<string> Allowed { get; set; } = [];
        public List<string> Keys { get; } = [];
        public Task<UserPermissionProjection?> GetProjectionAsync(Guid userId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<bool> HasPermissionAsync(Guid userId, string permissionKey, CancellationToken cancellationToken = default)
        {
            Keys.Add(permissionKey);
            return Task.FromResult(Allowed.Contains(permissionKey));
        }
    }
}
