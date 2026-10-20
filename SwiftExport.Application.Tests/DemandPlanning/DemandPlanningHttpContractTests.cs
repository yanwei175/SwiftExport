using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.Apis.DemandPlanning;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.WebApi.Controllers.DemandPlanning.Sources.Query;
using SwiftExport.WebApi.Controllers.DemandPlanning.Sources.Manual;
using SwiftExport.WebApi.Controllers.DemandPlanning.Sources.Delivery;
using SwiftExport.WebApi.Controllers.DemandPlanning.Materials.Query;
using SwiftExport.WebApi.Controllers.DemandPlanning.Materials.Establishment;
using SwiftExport.WebApi.Controllers.DemandPlanning.Materials.Reversal;
using SwiftExport.WebApi.Controllers.DemandPlanning.Materials.SupplierSuggestion;
using SwiftExport.WebApi.RequestProtection;

namespace SwiftExport.Application.Tests.DemandPlanning;

/// <summary>冻结需求模块 16 个 HTTP 动作的完整路径、绑定、返回类型、授权和 Refit 身份；旧物料追踪已退出。</summary>
[TestClass]
public sealed class DemandPlanningHttpContractTests
{
    /// <summary>按实施前逐项记录的契约核对拆分后的 Controller 与统一 Refit 接口。</summary>
    [TestMethod]
    public void SixteenActionsKeepHttpAndRefitContracts()
    {
        var cases = new (Type Controller, string Action, string Verb, string Prefix, string Template,
            Type Api, string ApiAction, bool Authorized, int? DuplicateWindow, string Bindings)[]
        {
            (typeof(GrossDemandQueryController), "SearchAsync", "POST", "api/demand-planning/gross-demands", "search",
                typeof(IGrossDemandWorkspaceApi), "SearchAsync", false, null, "body,none"),
            (typeof(GrossDemandQueryController), "SearchSourceGroupsAsync", "POST", "api/demand-planning/gross-demands", "source-groups/search",
                typeof(IGrossDemandWorkspaceApi), "SearchSourceGroupsAsync", false, null, "body,none"),
            (typeof(GrossDemandQueryController), "GetDetailAsync", "GET", "api/demand-planning/gross-demands", "{uid:guid}",
                typeof(IGrossDemandWorkspaceApi), "GetDetailAsync", false, null, "route,none"),
            (typeof(GrossDemandQueryController), "GetCustomerOptionsAsync", "GET", "api/demand-planning/gross-demands", "customer-options",
                typeof(IGrossDemandWorkspaceApi), "GetCustomerOptionsAsync", false, null, "none"),
            (typeof(ManualGrossDemandController), "CreateManualAsync", "POST", "api/demand-planning/gross-demands", "manual",
                typeof(IGrossDemandWorkspaceApi), "CreateManualAsync", false, 0, "body"),
            (typeof(ManualGrossDemandController), "UpdateManualAsync", "PUT", "api/demand-planning/gross-demands", "manual/{uid:guid}",
                typeof(IGrossDemandWorkspaceApi), "UpdateManualAsync", false, 0, "route,body"),
            (typeof(ManualGrossDemandController), "CloseManualAsync", "POST", "api/demand-planning/gross-demands", "manual/{uid:guid}/close",
                typeof(IGrossDemandWorkspaceApi), "CloseManualAsync", false, 0, "route,body"),
            (typeof(MaterialDemandQueryController), "SearchManagerAsync", "POST", "api/demand-planning/net-demands", "material-search",
                typeof(INetDemandManagerApi), "SearchAsync", false, null, "body,none"),
            (typeof(MaterialDemandQueryController), "SearchMaterialSourceGroupsAsync", "POST", "api/demand-planning/net-demands", "material-source-groups",
                typeof(INetDemandManagerApi), "SearchSourceGroupsAsync", false, null, "body,none"),
            (typeof(MaterialDemandQueryController), "GetSourceDemandCandidateGroupsAsync", "GET", "api/demand-planning/net-demands", "source-demands/candidate-groups",
                typeof(INetDemandManagerApi), "GetSourceDemandCandidateGroupsAsync", false, null, "none"),
            (typeof(MaterialDemandQueryController), "GetSourceDemandCandidatesBySourceAsync", "GET", "api/demand-planning/net-demands", "source-demands/candidates/{sourceType}/{sourceUid:guid}",
                typeof(INetDemandManagerApi), "GetSourceDemandCandidatesBySourceAsync", false, null, "none,none,none"),
            (typeof(MaterialDemandSupplierSuggestionController), "UpdateSuggestedPurchaseSupplierAsync", "POST", "api/demand-planning/net-demands", "suggested-purchase-supplier",
                typeof(INetDemandManagerApi), "UpdateSuggestedPurchaseSupplierAsync", false, null, "body,none"),
            (typeof(MaterialDemandEstablishmentController), "EstablishFromSourcesAsync", "POST", "api/demand-planning/net-demands", "source-demands/establish",
                typeof(INetDemandManagerApi), "EstablishFromSourcesAsync", false, null, "body,none"),
            (typeof(MaterialDemandReversalController), "RollbackSourceEstablishmentAsync", "POST", "api/demand-planning/net-demands", "source-demands/rollback",
                typeof(INetDemandManagerApi), "RollbackSourceEstablishmentAsync", false, null, "body,none"),
            (typeof(SourceDemandDeliveryController), "SearchAsync", "POST", "api/demand-planning/source-demands/delivery", "search",
                typeof(ISourceDemandDeliveryApi), "SearchAsync", true, null, "body,none"),
            (typeof(SourceDemandDeliveryController), "GetAsync", "GET", "api/demand-planning/source-demands/delivery", "{grossDemandId:guid}",
                typeof(ISourceDemandDeliveryApi), "GetAsync", true, null, "route,none")
        };

        Assert.HasCount(16, cases);
        Assert.AreEqual(16, cases.Select(item => item.Controller).Distinct()
            .SelectMany(controller => controller.GetMethods(System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
            .Count(method => method.IsDefined(typeof(HttpMethodAttribute), false)));
        Assert.AreEqual(16, cases.Select(item => (item.Verb, item.Prefix, item.Template)).Distinct().Count());
        foreach (var item in cases)
        {
            var action = item.Controller.GetMethod(item.Action)!;
            var apiAction = item.Api.GetMethod(item.ApiAction)!;
            Assert.IsNotNull(action, item.Action);
            Assert.IsNotNull(apiAction, item.ApiAction);
            Assert.AreEqual(item.Prefix, item.Controller.GetCustomAttributes(typeof(RouteAttribute), false)
                .Cast<RouteAttribute>().Single().Template, item.Action);
            var http = action.GetCustomAttributes(typeof(HttpMethodAttribute), false)
                .Cast<HttpMethodAttribute>().Single();
            Assert.AreEqual(item.Verb, http.HttpMethods.Single(), item.Action);
            Assert.AreEqual(item.Template, http.Template, item.Action);
            Assert.AreEqual(item.Authorized, item.Controller.IsDefined(typeof(AuthorizeAttribute), false), item.Action);
            var duplicate = action.GetCustomAttributes(typeof(PreventDuplicateRequestAttribute), false)
                .Cast<PreventDuplicateRequestAttribute>().SingleOrDefault();
            Assert.AreEqual(item.DuplicateWindow.HasValue, duplicate is not null, item.Action);
            if (item.DuplicateWindow == 3)
                Assert.AreEqual(3, duplicate!.WindowSeconds, item.Action);

            var attributeName = item.Verb switch
            {
                "GET" => "GetAttribute",
                "PUT" => "PutAttribute",
                _ => "PostAttribute"
            };
            var apiHttp = apiAction.GetCustomAttributes(false).Single(attribute =>
                attribute.GetType().Name == attributeName);
            var apiPath = (string)apiHttp.GetType().GetProperty("Path")!.GetValue(apiHttp)!;
            Assert.AreEqual("/" + item.Template.Replace(":guid", ""), apiPath, item.Action);
            CollectionAssert.AreEqual(action.GetParameters().Select(parameter => parameter.ParameterType).ToArray(),
                apiAction.GetParameters().Select(parameter => parameter.ParameterType).ToArray(), item.Action);
            var bindings = action.GetParameters().Select(parameter =>
                parameter.IsDefined(typeof(FromBodyAttribute), false) ? "body" :
                parameter.IsDefined(typeof(FromRouteAttribute), false) ? "route" : "none");
            Assert.AreEqual(item.Bindings, string.Join(",", bindings), item.Action);
            CollectionAssert.AreEqual(
                action.GetParameters().Select(parameter => parameter.IsDefined(typeof(FromBodyAttribute), false)).ToArray(),
                apiAction.GetParameters().Select(parameter => parameter.IsDefined(typeof(Refit.BodyAttribute), false)).ToArray(),
                item.Action);

            var actionResult = action.ReturnType.GenericTypeArguments.Single();
            if (actionResult.IsGenericType && actionResult.GetGenericTypeDefinition() == typeof(ActionResult<>))
                actionResult = actionResult.GenericTypeArguments.Single();
            Assert.AreEqual(apiAction.ReturnType.GenericTypeArguments.Single(), actionResult, item.Action);
        }
    }

    /// <summary>手工请求保持公开命名空间、业务字段及用于幂等重试的 CommandId 契约。</summary>
    [TestMethod]
    public void ManualRequestsKeepPublicShape()
    {
        Assert.AreEqual("SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand",
            typeof(CreateManualGrossDemandRequest).Namespace);
        CollectionAssert.AreEquivalent(
            new[] { "CommandId", "ProductId", "IsNonStandard", "Sku", "LinkSku", "CustomerSku", "Description",
                "Color", "Specification", "ImagePath", "DemandQty", "RequiredDate", "Remark" },
            typeof(CreateManualGrossDemandRequest).GetProperties().Select(property => property.Name).ToArray());
        CollectionAssert.AreEquivalent(
            new[] { "CommandId", "Uid", "Version", "ProductId", "IsNonStandard", "Sku", "LinkSku", "CustomerSku",
                "Description", "Color", "Specification", "ImagePath", "DemandQty", "RequiredDate", "Remark" },
            typeof(UpdateManualGrossDemandRequest).GetProperties().Select(property => property.Name).ToArray());
        CollectionAssert.AreEquivalent(new[] { "CommandId", "Uid", "Version", "Reason" },
            typeof(CloseGrossDemandCommand).GetProperties().Select(property => property.Name).ToArray());
        foreach (var requestType in new[] { typeof(CreateManualGrossDemandRequest), typeof(UpdateManualGrossDemandRequest), typeof(CloseGrossDemandCommand) })
            Assert.AreEqual(typeof(Guid), requestType.GetProperty("CommandId")!.PropertyType, requestType.Name);
    }
}
