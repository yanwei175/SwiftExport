using Refit;
using SwiftExport.Contracts.Apis.DemandPlanning;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.SysEnums;
using System.Reflection;

namespace SwiftExport.Client.Wpf.Tests.Features.DemandPlanning.GrossDemand;

[TestClass]
public sealed class GrossDemandApiContractTests
{
    [TestMethod]
    public void GrossDemandApi_ShouldExposeFrozenRoutesAndBodies()
    {
        AssertRoute(nameof(IGrossDemandWorkspaceApi.SearchAsync), typeof(PostAttribute), "/search", 0);
        AssertRoute(nameof(IGrossDemandWorkspaceApi.GetDetailAsync), typeof(GetAttribute), "/{uid}");
        AssertRoute(nameof(IGrossDemandWorkspaceApi.GetTraceAsync), typeof(GetAttribute), "/{uid}/trace");
        AssertRoute(nameof(IGrossDemandWorkspaceApi.DecomposeAsync), typeof(PostAttribute), "/decompose", 0);
        AssertRoute(nameof(IGrossDemandWorkspaceApi.RollbackAsync), typeof(PostAttribute), "/rollback-decomposition", 0);
        AssertRoute(nameof(IGrossDemandWorkspaceApi.CreateManualAsync), typeof(PostAttribute), "/manual", 0);
        AssertRoute(nameof(IGrossDemandWorkspaceApi.UpdateManualAsync), typeof(PutAttribute), "/manual/{uid}", 1);
        AssertRoute(nameof(IGrossDemandWorkspaceApi.CloseManualAsync), typeof(PostAttribute), "/manual/{uid}/close", 1);
    }

    [TestMethod]
    public void ReadMethods_ShouldExposeCancellationToken_ButWriteMethodsShouldNot()
    {
        AssertHasCancellationToken(nameof(IGrossDemandWorkspaceApi.SearchAsync));
        AssertHasCancellationToken(nameof(IGrossDemandWorkspaceApi.GetDetailAsync));
        AssertHasCancellationToken(nameof(IGrossDemandWorkspaceApi.GetTraceAsync));

        AssertHasNoCancellationToken(nameof(IGrossDemandWorkspaceApi.DecomposeAsync));
        AssertHasNoCancellationToken(nameof(IGrossDemandWorkspaceApi.RollbackAsync));
        AssertHasNoCancellationToken(nameof(IGrossDemandWorkspaceApi.CreateManualAsync));
        AssertHasNoCancellationToken(nameof(IGrossDemandWorkspaceApi.UpdateManualAsync));
        AssertHasNoCancellationToken(nameof(IGrossDemandWorkspaceApi.CloseManualAsync));
    }

    [TestMethod]
    public void CommandItem_ShouldCarryConcurrencyAndClientCorrelationFields()
    {
        var properties = typeof(GrossDemandCommandItem).GetProperties().Select(property => property.Name).ToArray();

        CollectionAssert.Contains(properties, nameof(GrossDemandCommandItem.Uid));
        CollectionAssert.Contains(properties, nameof(GrossDemandCommandItem.Version));
        CollectionAssert.Contains(properties, nameof(GrossDemandCommandItem.ExpectedDecompositionRevision));
        CollectionAssert.Contains(properties, nameof(GrossDemandCommandItem.ClientKey));
        CollectionAssert.Contains(properties, nameof(GrossDemandCommandItem.RowIndex));
    }

    [TestMethod]
    public void ListItem_ShouldNotLeakClientSelectionOrDirtyState()
    {
        var properties = typeof(GrossDemandListItemDto).GetProperties().Select(property => property.Name).ToArray();

        CollectionAssert.DoesNotContain(properties, "IsChecked");
        CollectionAssert.DoesNotContain(properties, "IsSelected");
        CollectionAssert.DoesNotContain(properties, "IsDirty");
    }

    [TestMethod]
    public void PersistedEnumValues_ShouldRemainStable()
    {
        CollectionAssert.AreEqual(
            new[] { 0, 8, 9 },
            Enum.GetValues<GrossDemandLifecycleStatusEnum>().Select(value => (int)value).ToArray());
        CollectionAssert.AreEqual(
            new[] { 0, 1 },
            Enum.GetValues<GrossDemandDecompositionStatusEnum>().Select(value => (int)value).ToArray());
        CollectionAssert.AreEqual(
            new[] { 0, 8, 9 },
            Enum.GetValues<NetDemandLifecycleStatusEnum>().Select(value => (int)value).ToArray());
        CollectionAssert.AreEqual(
            new[] { 0, 8, 9 },
            Enum.GetValues<DemandExecutionLinkStatusEnum>().Select(value => (int)value).ToArray());
    }

    private static void AssertRoute(string methodName, Type attributeType, string path, int? bodyParameterIndex = null)
    {
        var method = typeof(IGrossDemandWorkspaceApi).GetMethod(methodName);
        Assert.IsNotNull(method);

        var attribute = method.GetCustomAttributes(attributeType, false).Single();
        var actualPath = (string)attributeType.GetProperty(nameof(HttpMethodAttribute.Path))!.GetValue(attribute)!;
        Assert.AreEqual(path, actualPath);

        if (bodyParameterIndex.HasValue)
        {
            var body = method.GetParameters()[bodyParameterIndex.Value]
                .GetCustomAttributes(typeof(BodyAttribute), false)
                .SingleOrDefault();
            Assert.IsNotNull(body, $"{methodName} 的请求体参数缺少 [Body]。");
        }
    }

    private static void AssertHasCancellationToken(string methodName)
    {
        var method = typeof(IGrossDemandWorkspaceApi).GetMethod(methodName);
        Assert.IsNotNull(method);
        Assert.IsTrue(method.GetParameters().Any(parameter => parameter.ParameterType == typeof(CancellationToken)));
    }

    private static void AssertHasNoCancellationToken(string methodName)
    {
        var method = typeof(IGrossDemandWorkspaceApi).GetMethod(methodName);
        Assert.IsNotNull(method);
        Assert.IsFalse(method.GetParameters().Any(parameter => parameter.ParameterType == typeof(CancellationToken)));
    }
}
