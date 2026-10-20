using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Contracts.Apis.DemandPlanning;
using SwiftExport.Contracts.Apis.Procurement;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using System.Reflection;

namespace SwiftExport.Client.Wpf.Tests.Features.DemandPlanning.NetDemand;

/// <summary>验证物料管理与采购契约边界，旧计划接口不得重新暴露。</summary>
[TestClass]
public sealed class NetDemandApiContractTests
{
    /// <summary>单行推荐供应商命令携带稳定身份，不改变原有 Refit 方法。</summary>
    [TestMethod]
    public void SupplierSuggestionCommand_ShouldCarryStableCommandId()
    {
        Assert.IsNotNull(typeof(UpdateSuggestedPurchaseSupplierCommand).GetProperty("CommandId"));
    }
    /// <summary>来源追溯退为空壳后，不再暴露独占远程查询及传输模型。</summary>
    [TestMethod]
    public void SourceTrace_ShouldNotExposeRemovedContract()
    {
        Assert.IsNull(typeof(INetDemandManagerApi).GetMethod("GetSourceDemandTraceAsync"));
        foreach (var name in new[] { "GrossDemandTraceDto", "NetDemandTraceItemDto", "DemandExecutionTraceItemDto" })
            Assert.IsNull(typeof(INetDemandManagerApi).Assembly.GetType(
                "SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand." + name));
    }

    /// <summary>物料跟踪统一使用订单跟踪契约，旧独占接口和模型退役。</summary>
    [TestMethod]
    public void NetDemandTrace_ShouldExposeNewReadOnlyContractWithoutRestoringLegacyDto()
    {
        Assert.IsNull(typeof(INetDemandManagerApi).GetMethod("GetTraceAsync"));
        foreach (var name in new[] { "MaterialDemandTraceDetailDto", "MaterialDemandTracePurchaseDto", "MaterialDemandTraceTreeItemDto" })
            Assert.IsNull(typeof(INetDemandManagerApi).Assembly.GetType("SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand.Trace." + name));
        Assert.IsNull(typeof(INetDemandManagerApi).Assembly.GetType(
            "SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand.NetDemandTraceDto"));
    }

    [TestMethod]
    public void MaterialWorkspaceApi_ShouldExposeAllocationFreeQueryRoutes()
    {
        AssertRoute<INetDemandManagerApi>(nameof(INetDemandManagerApi.SearchAsync), typeof(PostAttribute), "/material-search", 0);
        AssertRoute<INetDemandManagerApi>(nameof(INetDemandManagerApi.SearchSourceGroupsAsync), typeof(PostAttribute), "/material-source-groups", 0);
        AssertRoute<INetDemandManagerApi>(nameof(INetDemandManagerApi.UpdateSuggestedPurchaseSupplierAsync), typeof(PostAttribute), "/suggested-purchase-supplier", 0);
        AssertRoute<INetDemandManagerApi>(nameof(INetDemandManagerApi.RollbackSourceEstablishmentAsync), typeof(PostAttribute), "/source-demands/rollback", 0);
        AssertHasCancellationToken<INetDemandManagerApi>(nameof(INetDemandManagerApi.SearchAsync));
        AssertHasCancellationToken<INetDemandManagerApi>(nameof(INetDemandManagerApi.SearchSourceGroupsAsync));
        AssertHasCancellationToken<INetDemandManagerApi>(nameof(INetDemandManagerApi.UpdateSuggestedPurchaseSupplierAsync));

        var itemProperties = typeof(MaterialDemandManagerItemDto).GetProperties().Select(property => property.Name).ToArray();
        CollectionAssert.Contains(itemProperties, nameof(MaterialDemandManagerItemDto.AssociatedPurchaseQty));
        CollectionAssert.Contains(itemProperties, nameof(MaterialDemandManagerItemDto.SuggestedPurchaseSupplierId));
        CollectionAssert.Contains(itemProperties, nameof(MaterialDemandManagerItemDto.SuggestedDeliverySupplierId));
        CollectionAssert.DoesNotContain(itemProperties, "AllocationId");
        CollectionAssert.DoesNotContain(itemProperties, "PlannedQty");
        CollectionAssert.Contains(itemProperties, nameof(MaterialDemandManagerItemDto.RemainingQty));
        CollectionAssert.DoesNotContain(itemProperties, "BlockerSummary");

        var requestProperties = typeof(MaterialDemandManagerSearchRequest).GetProperties().Select(property => property.Name).ToArray();
        CollectionAssert.DoesNotContain(requestProperties, "PlanStatuses");
        CollectionAssert.DoesNotContain(requestProperties, "HasRemainingQty");
        CollectionAssert.DoesNotContain(requestProperties, "PurchaseSupplierIds");

        var updateProperties = typeof(UpdateSuggestedPurchaseSupplierCommand).GetProperties()
            .Select(property => property.Name).ToArray();
        CollectionAssert.AreEquivalent(new[]
        {
            nameof(UpdateSuggestedPurchaseSupplierCommand.CommandId),
            nameof(UpdateSuggestedPurchaseSupplierCommand.NetDemandId),
            nameof(UpdateSuggestedPurchaseSupplierCommand.Version),
            nameof(UpdateSuggestedPurchaseSupplierCommand.SuggestedPurchaseSupplierId)
        }, updateProperties);
    }

    [TestMethod]
    public void NetDemandApi_ShouldExposeSourceEstablishmentWithoutLegacyPlans()
    {
        AssertRoute(nameof(INetDemandManagerApi.SearchAsync), typeof(PostAttribute), "/material-search", 0);
        AssertRoute(nameof(INetDemandManagerApi.SearchSourceGroupsAsync), typeof(PostAttribute), "/material-source-groups", 0);
        AssertRoute(nameof(INetDemandManagerApi.GetSourceDemandCandidateGroupsAsync), typeof(GetAttribute), "/source-demands/candidate-groups");
        AssertRoute(nameof(INetDemandManagerApi.GetSourceDemandCandidatesBySourceAsync), typeof(GetAttribute), "/source-demands/candidates/{sourceType}/{sourceUid}");
        AssertRoute(nameof(INetDemandManagerApi.EstablishFromSourcesAsync), typeof(PostAttribute), "/source-demands/establish", 0);
        AssertRoute(nameof(INetDemandManagerApi.RollbackSourceEstablishmentAsync), typeof(PostAttribute), "/source-demands/rollback", 0);
        foreach (var name in new[] { "GetDetailAsync", "ReplacePlanAsync", "ApplyDefaultSupplierAsync", "BatchAssignSupplierAsync" })
            Assert.IsNull(typeof(INetDemandManagerApi).GetMethod(name));
    }

    [TestMethod]
    public void ReadMethods_ShouldSupportCancellation_AndWritesShouldCarryConcurrency()
    {
        AssertHasCancellationToken(nameof(INetDemandManagerApi.SearchAsync));
        AssertHasCancellationToken(nameof(INetDemandManagerApi.SearchSourceGroupsAsync));
        AssertHasCancellationToken(nameof(INetDemandManagerApi.GetSourceDemandCandidateGroupsAsync));
        AssertHasCancellationToken(nameof(INetDemandManagerApi.GetSourceDemandCandidatesBySourceAsync));
        AssertHasCancellationToken(nameof(INetDemandManagerApi.EstablishFromSourcesAsync));
        AssertHasCancellationToken(nameof(INetDemandManagerApi.RollbackSourceEstablishmentAsync));
        Assert.IsNotNull(typeof(EstablishMaterialDemandsCommand).GetProperty("CommandId"));
        Assert.IsNotNull(typeof(EstablishMaterialDemandItem).GetProperty("SourceDemandVersion"));
        Assert.IsNotNull(typeof(RollbackMaterialDemandsCommand).GetProperty("CommandId"));
        Assert.IsNotNull(typeof(RollbackMaterialDemandsCommand).GetProperty("Reason"));
    }

    [TestMethod]
    public void QueryDto_ShouldNotLeakClientSelectionOrDirtyState()
    {
        var properties = typeof(MaterialDemandManagerItemDto).GetProperties().Select(property => property.Name).ToArray();
        CollectionAssert.DoesNotContain(properties, "IsChecked");
        CollectionAssert.DoesNotContain(properties, "IsDirty");
        CollectionAssert.DoesNotContain(properties, "TransferQty");
    }

    [TestMethod]
    public void PurchaseDraftRoutes_ShouldBelongToProcurementApi()
    {
        AssertRoute<IPurchaseDraftApi>(nameof(IPurchaseDraftApi.GetCandidateSourceGroupsAsync), typeof(GetAttribute), "/candidates/source-groups");
        AssertRoute<IPurchaseDraftApi>(nameof(IPurchaseDraftApi.GetCandidatesAsync), typeof(PostAttribute), "/candidates", 0);
        AssertRoute<IPurchaseDraftApi>(nameof(IPurchaseDraftApi.GetAppendCandidatesAsync), typeof(GetAttribute), "/append-candidates");
        Assert.IsNull(typeof(IPurchaseDraftApi).GetMethod("PreviewAsync"));
        AssertRoute<IPurchaseDraftApi>(nameof(IPurchaseDraftApi.CreateAsync), typeof(PostAttribute), "/create", 0);
        Assert.IsNull(typeof(IPurchaseDraftApi).GetMethod("CancelAsync"));
        Assert.IsNull(typeof(IPurchaseDraftApi).GetMethod("PreviewReleaseAsync"));
        Assert.IsNull(typeof(IPurchaseDraftApi).GetMethod("ReleaseAsync"));
        Assert.IsNull(typeof(IPurchaseDraftApi).GetMethod("GetSourceTraceAsync"));
    }

    private static void AssertRoute(string methodName, Type attributeType, string path, int? bodyParameterIndex = null)
        => AssertRoute<INetDemandManagerApi>(methodName, attributeType, path, bodyParameterIndex);

    private static void AssertRoute<TApi>(string methodName, Type attributeType, string path, int? bodyParameterIndex = null)
    {
        var method = typeof(TApi).GetMethod(methodName);
        Assert.IsNotNull(method);
        var attribute = method.GetCustomAttributes(attributeType, false).Single();
        var actualPath = (string)attributeType.GetProperty(nameof(HttpMethodAttribute.Path))!.GetValue(attribute)!;
        Assert.AreEqual(path, actualPath);
        if (bodyParameterIndex.HasValue)
        {
            Assert.IsNotNull(method.GetParameters()[bodyParameterIndex.Value]
                .GetCustomAttributes(typeof(BodyAttribute), false)
                .SingleOrDefault());
        }
    }

    private static void AssertHasCancellationToken(string methodName)
        => AssertHasCancellationToken<INetDemandManagerApi>(methodName);

    private static void AssertHasCancellationToken<TApi>(string methodName)
    {
        var method = typeof(TApi).GetMethod(methodName);
        Assert.IsNotNull(method);
        Assert.IsTrue(method.GetParameters().Any(parameter => parameter.ParameterType == typeof(CancellationToken)));
    }
}
