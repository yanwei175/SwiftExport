using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Contracts.Apis.DemandPlanning;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using System.Reflection;

namespace SwiftExport.Client.Wpf.Tests.Features.DemandPlanning.NetDemand;

[TestClass]
public sealed class NetDemandApiContractTests
{
    [TestMethod]
    public void NetDemandApi_ShouldExposeQueryAndPlanRoutes()
    {
        AssertRoute(nameof(INetDemandWorkspaceApi.SearchAsync), typeof(PostAttribute), "/search", 0);
        AssertRoute(nameof(INetDemandWorkspaceApi.SearchSourceGroupsAsync), typeof(PostAttribute), "/source-groups", 0);
        AssertRoute(nameof(INetDemandWorkspaceApi.SearchSourceDemandCandidatesAsync), typeof(PostAttribute), "/source-demands/candidates/search", 0);
        AssertRoute(nameof(INetDemandWorkspaceApi.EstablishFromSourcesAsync), typeof(PostAttribute), "/source-demands/establish", 0);
        AssertRoute(nameof(INetDemandWorkspaceApi.RollbackSourceEstablishmentAsync), typeof(PostAttribute), "/source-demands/rollback", 0);
        AssertRoute(nameof(INetDemandWorkspaceApi.GetSourceDemandTraceAsync), typeof(GetAttribute), "/source-demands/{grossDemandId}/trace");
        AssertRoute(nameof(INetDemandWorkspaceApi.GetDetailAsync), typeof(GetAttribute), "/{uid}");
        AssertRoute(nameof(INetDemandWorkspaceApi.GetTraceAsync), typeof(GetAttribute), "/{uid}/trace");
        AssertRoute(nameof(INetDemandWorkspaceApi.ReplacePlanAsync), typeof(PostAttribute), "/plans/replace", 0);
        AssertRoute(nameof(INetDemandWorkspaceApi.ApplyDefaultSupplierAsync), typeof(PostAttribute), "/plans/apply-product-default", 0);
        AssertRoute(nameof(INetDemandWorkspaceApi.BatchAssignSupplierAsync), typeof(PostAttribute), "/plans/batch-assign-supplier", 0);
        AssertRoute(nameof(INetDemandWorkspaceApi.PreviewPurchaseDraftsAsync), typeof(PostAttribute), "/purchase-drafts/preview", 0);
        AssertRoute(nameof(INetDemandWorkspaceApi.CreatePurchaseDraftsAsync), typeof(PostAttribute), "/purchase-drafts/create", 0);
        AssertRoute(nameof(INetDemandWorkspaceApi.CancelPurchaseDraftAsync), typeof(PostAttribute), "/purchase-drafts/{uid}/cancel", 1);
        AssertRoute(nameof(INetDemandWorkspaceApi.PreviewPurchaseDraftReleaseAsync), typeof(PostAttribute), "/purchase-drafts/{uid}/release-preview", 1);
        AssertRoute(nameof(INetDemandWorkspaceApi.ReleasePurchaseDraftAsync), typeof(PostAttribute), "/purchase-drafts/{uid}/release", 1);
        AssertRoute(nameof(INetDemandWorkspaceApi.GetPurchaseTraceBySourceAsync), typeof(GetAttribute), "/sources/{sourceType}/{sourceUid}/purchase-trace");
    }

    [TestMethod]
    public void ReadMethods_ShouldSupportCancellation_AndWritesShouldCarryConcurrency()
    {
        AssertHasCancellationToken(nameof(INetDemandWorkspaceApi.SearchAsync));
        AssertHasCancellationToken(nameof(INetDemandWorkspaceApi.SearchSourceGroupsAsync));
        AssertHasCancellationToken(nameof(INetDemandWorkspaceApi.SearchSourceDemandCandidatesAsync));
        AssertHasCancellationToken(nameof(INetDemandWorkspaceApi.EstablishFromSourcesAsync));
        AssertHasCancellationToken(nameof(INetDemandWorkspaceApi.RollbackSourceEstablishmentAsync));
        AssertHasCancellationToken(nameof(INetDemandWorkspaceApi.GetSourceDemandTraceAsync));
        AssertHasCancellationToken(nameof(INetDemandWorkspaceApi.GetDetailAsync));
        AssertHasCancellationToken(nameof(INetDemandWorkspaceApi.GetTraceAsync));
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
    public void PurchaseDraftLifecycleRoutes_ShouldBelongToNetDemandWorkspaceApi()
    {
        AssertHasCancellationToken(nameof(INetDemandWorkspaceApi.CancelPurchaseDraftAsync));
        AssertHasCancellationToken(nameof(INetDemandWorkspaceApi.PreviewPurchaseDraftReleaseAsync));
        AssertHasCancellationToken(nameof(INetDemandWorkspaceApi.ReleasePurchaseDraftAsync));
    }

    private static void AssertRoute(string methodName, Type attributeType, string path, int? bodyParameterIndex = null)
    {
        var method = typeof(INetDemandWorkspaceApi).GetMethod(methodName);
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
    {
        var method = typeof(INetDemandWorkspaceApi).GetMethod(methodName);
        Assert.IsNotNull(method);
        Assert.IsTrue(method.GetParameters().Any(parameter => parameter.ParameterType == typeof(CancellationToken)));
    }
}
