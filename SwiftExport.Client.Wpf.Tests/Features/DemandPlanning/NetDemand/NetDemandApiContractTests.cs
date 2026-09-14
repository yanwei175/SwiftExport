using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Contracts.Apis.DemandPlanning;
using SwiftExport.Contracts.Apis.Procurement;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using System.Reflection;

namespace SwiftExport.Client.Wpf.Tests.Features.DemandPlanning.NetDemand;

[TestClass]
public sealed class NetDemandApiContractTests
{
    /// <summary>来源追溯退为空壳后，不再暴露独占远程查询及传输模型。</summary>
    [TestMethod]
    public void SourceTrace_ShouldNotExposeRemovedContract()
    {
        Assert.IsNull(typeof(INetDemandWorkspaceApi).GetMethod("GetSourceDemandTraceAsync"));
        foreach (var name in new[] { "GrossDemandTraceDto", "NetDemandTraceItemDto", "DemandExecutionTraceItemDto" })
            Assert.IsNull(typeof(INetDemandWorkspaceApi).Assembly.GetType(
                "SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand." + name));
    }

    /// <summary>净需求追溯退为空壳后，不再暴露独占远程查询及传输模型。</summary>
    [TestMethod]
    public void NetDemandTrace_ShouldNotExposeRemovedContract()
    {
        Assert.IsNull(typeof(INetDemandWorkspaceApi).GetMethod("GetTraceAsync"));
        Assert.IsNull(typeof(INetDemandWorkspaceApi).Assembly.GetType(
            "SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand.NetDemandTraceDto"));
    }

    [TestMethod]
    public void MaterialWorkspaceApi_ShouldExposeAllocationFreeQueryRoutes()
    {
        AssertRoute<IMaterialDemandWorkspaceApi>(nameof(IMaterialDemandWorkspaceApi.SearchAsync), typeof(PostAttribute), "/material-search", 0);
        AssertRoute<IMaterialDemandWorkspaceApi>(nameof(IMaterialDemandWorkspaceApi.SearchSourceGroupsAsync), typeof(PostAttribute), "/material-source-groups", 0);
        AssertRoute<IMaterialDemandWorkspaceApi>(nameof(IMaterialDemandWorkspaceApi.UpdateSuggestedPurchaseSupplierAsync), typeof(PostAttribute), "/suggested-purchase-supplier", 0);
        AssertRoute<IMaterialDemandWorkspaceApi>(nameof(IMaterialDemandWorkspaceApi.RollbackSourceEstablishmentAsync), typeof(PostAttribute), "/source-demands/rollback", 0);
        AssertHasCancellationToken<IMaterialDemandWorkspaceApi>(nameof(IMaterialDemandWorkspaceApi.SearchAsync));
        AssertHasCancellationToken<IMaterialDemandWorkspaceApi>(nameof(IMaterialDemandWorkspaceApi.SearchSourceGroupsAsync));
        AssertHasCancellationToken<IMaterialDemandWorkspaceApi>(nameof(IMaterialDemandWorkspaceApi.UpdateSuggestedPurchaseSupplierAsync));

        var itemProperties = typeof(MaterialDemandWorkspaceItemDto).GetProperties().Select(property => property.Name).ToArray();
        CollectionAssert.Contains(itemProperties, nameof(MaterialDemandWorkspaceItemDto.AssociatedPurchaseQty));
        CollectionAssert.Contains(itemProperties, nameof(MaterialDemandWorkspaceItemDto.SuggestedPurchaseSupplierId));
        CollectionAssert.Contains(itemProperties, nameof(MaterialDemandWorkspaceItemDto.SuggestedDeliverySupplierId));
        CollectionAssert.DoesNotContain(itemProperties, "AllocationId");
        CollectionAssert.DoesNotContain(itemProperties, "PlannedQty");
        CollectionAssert.Contains(itemProperties, nameof(MaterialDemandWorkspaceItemDto.RemainingQty));
        CollectionAssert.DoesNotContain(itemProperties, "BlockerSummary");

        var requestProperties = typeof(MaterialDemandWorkspaceSearchRequest).GetProperties().Select(property => property.Name).ToArray();
        CollectionAssert.DoesNotContain(requestProperties, "PlanStatuses");
        CollectionAssert.DoesNotContain(requestProperties, "HasRemainingQty");
        CollectionAssert.DoesNotContain(requestProperties, "PurchaseSupplierIds");

        var updateProperties = typeof(UpdateSuggestedPurchaseSupplierCommand).GetProperties()
            .Select(property => property.Name).ToArray();
        CollectionAssert.AreEquivalent(new[]
        {
            nameof(UpdateSuggestedPurchaseSupplierCommand.NetDemandId),
            nameof(UpdateSuggestedPurchaseSupplierCommand.Version),
            nameof(UpdateSuggestedPurchaseSupplierCommand.SuggestedPurchaseSupplierId)
        }, updateProperties);
    }

    [TestMethod]
    public void NetDemandApi_ShouldExposeQueryAndPlanRoutes()
    {
        AssertRoute(nameof(INetDemandWorkspaceApi.SearchAsync), typeof(PostAttribute), "/search", 0);
        AssertRoute(nameof(INetDemandWorkspaceApi.SearchSourceGroupsAsync), typeof(PostAttribute), "/source-groups", 0);
        AssertRoute(nameof(INetDemandWorkspaceApi.SearchSourceDemandCandidatesAsync), typeof(PostAttribute), "/source-demands/candidates/search", 0);
        AssertRoute(nameof(INetDemandWorkspaceApi.GetSourceDemandCandidateGroupsAsync), typeof(GetAttribute), "/source-demands/candidate-groups");
        AssertRoute(nameof(INetDemandWorkspaceApi.GetSourceDemandCandidatesBySourceAsync), typeof(GetAttribute), "/source-demands/candidates/{sourceType}/{sourceUid}");
        AssertRoute(nameof(INetDemandWorkspaceApi.EstablishFromSourcesAsync), typeof(PostAttribute), "/source-demands/establish", 0);
        AssertRoute(nameof(INetDemandWorkspaceApi.RollbackSourceEstablishmentAsync), typeof(PostAttribute), "/source-demands/rollback", 0);
        AssertRoute(nameof(INetDemandWorkspaceApi.GetDetailAsync), typeof(GetAttribute), "/{uid}");
        AssertRoute(nameof(INetDemandWorkspaceApi.ReplacePlanAsync), typeof(PostAttribute), "/plans/replace", 0);
        AssertRoute(nameof(INetDemandWorkspaceApi.ApplyDefaultSupplierAsync), typeof(PostAttribute), "/plans/apply-product-default", 0);
        AssertRoute(nameof(INetDemandWorkspaceApi.BatchAssignSupplierAsync), typeof(PostAttribute), "/plans/batch-assign-supplier", 0);
    }

    [TestMethod]
    public void ReadMethods_ShouldSupportCancellation_AndWritesShouldCarryConcurrency()
    {
        AssertHasCancellationToken(nameof(INetDemandWorkspaceApi.SearchAsync));
        AssertHasCancellationToken(nameof(INetDemandWorkspaceApi.SearchSourceGroupsAsync));
        AssertHasCancellationToken(nameof(INetDemandWorkspaceApi.SearchSourceDemandCandidatesAsync));
        AssertHasCancellationToken(nameof(INetDemandWorkspaceApi.GetSourceDemandCandidateGroupsAsync));
        AssertHasCancellationToken(nameof(INetDemandWorkspaceApi.GetSourceDemandCandidatesBySourceAsync));
        AssertHasCancellationToken(nameof(INetDemandWorkspaceApi.EstablishFromSourcesAsync));
        AssertHasCancellationToken(nameof(INetDemandWorkspaceApi.RollbackSourceEstablishmentAsync));
        AssertHasCancellationToken(nameof(INetDemandWorkspaceApi.GetDetailAsync));
        Assert.IsFalse(typeof(INetDemandWorkspaceApi).GetMethod(nameof(INetDemandWorkspaceApi.ReplacePlanAsync))!
            .GetParameters().Any(parameter => parameter.ParameterType == typeof(CancellationToken)));

        var properties = typeof(ReplaceNetDemandPlanCommand).GetProperties().Select(property => property.Name).ToArray();
        CollectionAssert.Contains(properties, nameof(ReplaceNetDemandPlanCommand.CommandId));
        CollectionAssert.Contains(properties, nameof(ReplaceNetDemandPlanCommand.AllocationId));
        CollectionAssert.Contains(properties, nameof(ReplaceNetDemandPlanCommand.ExpectedVersion));
        CollectionAssert.Contains(properties, nameof(ReplaceNetDemandPlanCommand.Reason));
    }

    [TestMethod]
    public void QueryDto_ShouldNotLeakClientSelectionOrDirtyState()
    {
        var properties = typeof(NetDemandListItemDto).GetProperties().Select(property => property.Name).ToArray();
        CollectionAssert.DoesNotContain(properties, "IsChecked");
        CollectionAssert.DoesNotContain(properties, "IsDirty");
        CollectionAssert.DoesNotContain(properties, "TransferQty");
    }

    [TestMethod]
    public void PurchaseDraftRoutes_ShouldBelongToProcurementApi()
    {
        AssertRoute<IPurchaseDraftApi>(nameof(IPurchaseDraftApi.GetCandidateSourceGroupsAsync), typeof(GetAttribute), "/candidates/source-groups");
        AssertRoute<IPurchaseDraftApi>(nameof(IPurchaseDraftApi.GetCandidatesAsync), typeof(PostAttribute), "/candidates", 0);
        AssertRoute<IPurchaseDraftApi>(nameof(IPurchaseDraftApi.PreviewAsync), typeof(PostAttribute), "/preview", 0);
        AssertRoute<IPurchaseDraftApi>(nameof(IPurchaseDraftApi.CreateAsync), typeof(PostAttribute), "/create", 0);
        Assert.IsNull(typeof(IPurchaseDraftApi).GetMethod("CancelAsync"));
        Assert.IsNull(typeof(IPurchaseDraftApi).GetMethod("PreviewReleaseAsync"));
        Assert.IsNull(typeof(IPurchaseDraftApi).GetMethod("ReleaseAsync"));
        Assert.IsNull(typeof(IPurchaseDraftApi).GetMethod("GetSourceTraceAsync"));
    }

    private static void AssertRoute(string methodName, Type attributeType, string path, int? bodyParameterIndex = null)
        => AssertRoute<INetDemandWorkspaceApi>(methodName, attributeType, path, bodyParameterIndex);

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
        => AssertHasCancellationToken<INetDemandWorkspaceApi>(methodName);

    private static void AssertHasCancellationToken<TApi>(string methodName)
    {
        var method = typeof(TApi).GetMethod(methodName);
        Assert.IsNotNull(method);
        Assert.IsTrue(method.GetParameters().Any(parameter => parameter.ParameterType == typeof(CancellationToken)));
    }
}
