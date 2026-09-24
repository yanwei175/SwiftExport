using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Interfaces.DemandPlanning.Materials.Query;
using SwiftExport.Application.Interfaces.DemandPlanning.Materials.Sources;
using SwiftExport.Application.Interfaces.DemandPlanning.Materials.SupplierSuggestion;
using SwiftExport.Application.Interfaces.DemandPlanning.Materials.Establishment;
using SwiftExport.Application.Interfaces.DemandPlanning.Materials.Reversal;
using SwiftExport.Application.Services.DemandPlanning.Materials.Query;
using SwiftExport.Application.Services.DemandPlanning.Materials.Sources;
using SwiftExport.Application.Services.DemandPlanning.Materials.SupplierSuggestion;
using SwiftExport.Application.Services.DemandPlanning.Materials.Establishment;
using SwiftExport.Application.Services.DemandPlanning.Materials.Reversal;
using SwiftExport.WebApi.Controllers.DemandPlanning.Materials.Query;
using SwiftExport.WebApi.Controllers.DemandPlanning.Materials.SupplierSuggestion;
using SwiftExport.WebApi.Controllers.DemandPlanning.Materials.Establishment;
using SwiftExport.WebApi.Controllers.DemandPlanning.Materials.Reversal;

namespace SwiftExport.Application.Tests.DemandPlanning;

/// <summary>验证物料 HTTP 用例分别转发给窄服务，且旧宽边界已经退出。</summary>
[TestClass]
public sealed class NetDemandManagerServiceRoutingTests
{
    /// <summary>全部物料 action 必须调用所属窄接口并完整传递参数。</summary>
    [TestMethod]
    public void MaterialActions_ForwardToNewService()
    {
        var query = DispatchProxy.Create<IMaterialDemandQueryService, ForwardingProbe>();
        var sources = DispatchProxy.Create<IMaterialDemandSourceQueryService, ForwardingProbe>();
        var suggestion = DispatchProxy.Create<IMaterialDemandSupplierSuggestionService, ForwardingProbe>();
        var establishment = DispatchProxy.Create<IMaterialDemandEstablishmentService, ForwardingProbe>();
        var reversal = DispatchProxy.Create<IMaterialDemandReversalService, ForwardingProbe>();
        var queryController = new MaterialDemandQueryController(query, sources);
        var suggestionController = new MaterialDemandSupplierSuggestionController(suggestion);
        var establishmentController = new MaterialDemandEstablishmentController(establishment);
        var reversalController = new MaterialDemandReversalController(reversal);

        foreach (var method in typeof(IMaterialDemandQueryService).GetMethods())
            VerifyForwarding(queryController, method.Name, method, (ForwardingProbe)(object)query);
        foreach (var method in typeof(IMaterialDemandSourceQueryService).GetMethods())
            VerifyForwarding(queryController, method.Name, method, (ForwardingProbe)(object)sources);
        VerifyForwarding(suggestionController, nameof(IMaterialDemandSupplierSuggestionService.UpdateSuggestedPurchaseSupplierAsync),
            typeof(IMaterialDemandSupplierSuggestionService).GetMethod(nameof(IMaterialDemandSupplierSuggestionService.UpdateSuggestedPurchaseSupplierAsync))!,
            (ForwardingProbe)(object)suggestion);
        VerifyForwarding(establishmentController, nameof(MaterialDemandEstablishmentController.EstablishFromSourcesAsync),
            typeof(IMaterialDemandEstablishmentService).GetMethod(nameof(IMaterialDemandEstablishmentService.EstablishAsync))!,
            (ForwardingProbe)(object)establishment);
        VerifyForwarding(reversalController, nameof(MaterialDemandReversalController.RollbackSourceEstablishmentAsync),
            typeof(IMaterialDemandReversalService).GetMethod(nameof(IMaterialDemandReversalService.RollbackAsync))!,
            (ForwardingProbe)(object)reversal);

    }

    /// <summary>已退出的旧计划 action 不能重新进入当前物料事实边界。</summary>
    [TestMethod]
    public void LegacyActions_AreRemoved()
    {
        foreach (var name in new[] { "ReplacePlanAsync", "ApplyDefaultSupplierAsync", "BatchAssignSupplierAsync", "GetDetailAsync" })
        {
            foreach (var controller in new[] { typeof(MaterialDemandQueryController), typeof(MaterialDemandSupplierSuggestionController),
                typeof(MaterialDemandEstablishmentController), typeof(MaterialDemandReversalController) })
                Assert.IsNull(controller.GetMethod(name));
            foreach (var port in new[] { typeof(IMaterialDemandQueryService), typeof(IMaterialDemandSourceQueryService),
                typeof(IMaterialDemandSupplierSuggestionService), typeof(IMaterialDemandEstablishmentService), typeof(IMaterialDemandReversalService) })
                Assert.IsNull(port.GetMethod(name));
        }
    }

    /// <summary>只注册请求级窄服务，不遗留 Manager 与旧计划服务。</summary>
    [TestMethod]
    public void ApplicationRegistration_RegistersNarrowServicesOnly()
    {
        var services = new ServiceCollection();
        services.AddApplicationServices();
        foreach (var pair in new[]
        {
            (typeof(IMaterialDemandQueryService), typeof(MaterialDemandQueryService)),
            (typeof(IMaterialDemandSourceQueryService), typeof(MaterialDemandSourceQueryService)),
            (typeof(IMaterialDemandSupplierSuggestionService), typeof(MaterialDemandSupplierSuggestionService)),
            (typeof(IMaterialDemandEstablishmentService), typeof(MaterialDemandEstablishmentService)),
            (typeof(IMaterialDemandReversalService), typeof(MaterialDemandReversalService))
        })
        {
            var registration = services.Single(item => item.ServiceType == pair.Item1);
            Assert.AreEqual(pair.Item2, registration.ImplementationType);
            Assert.AreEqual(ServiceLifetime.Scoped, registration.Lifetime);
        }
        Assert.IsFalse(services.Any(item => item.ServiceType.Name == "INetDemandManagerService"));
        Assert.IsFalse(services.Any(item => item.ServiceType.Name == "INetDemandWorkspaceService"));
    }

    /// <summary>真实调用 Controller 并检查目标、所有参数及返回任务，避免只搜索源码文本。</summary>
    private static void VerifyForwarding(object controller, string actionName, MethodInfo method, ForwardingProbe probe)
    {
        using var cancellation = new CancellationTokenSource();
        var arguments = method.GetParameters().Select(parameter =>
            parameter.ParameterType == typeof(CancellationToken) ? (object)cancellation.Token :
            parameter.ParameterType == typeof(Guid) ? Guid.NewGuid() :
            parameter.ParameterType == typeof(string) ? "SO" :
            Activator.CreateInstance(parameter.ParameterType)).ToArray();
        var before = probe.CallCount;
        var result = controller.GetType().GetMethod(actionName)!.Invoke(controller, arguments);

        Assert.AreEqual(before + 1, probe.CallCount, method.Name);
        Assert.AreEqual(method.Name, probe.MethodName);
        CollectionAssert.AreEqual(arguments, probe.Arguments!);
        Assert.AreSame(probe.Result, result);
    }

    /// <summary>仅记录接口调用并返回已完成任务；不连接数据库或执行采购业务。</summary>
    public class ForwardingProbe : DispatchProxy
    {
        public int CallCount { get; private set; }
        public string? MethodName { get; private set; }
        public object?[]? Arguments { get; private set; }
        public object? Result { get; private set; }

        /// <summary>捕获被转发的方法和参数，创建匹配返回类型的任务供身份断言。</summary>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            CallCount++;
            MethodName = targetMethod!.Name;
            Arguments = args;
            var resultType = targetMethod.ReturnType.GetGenericArguments().Single();
            Result = typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(resultType)
                .Invoke(null, [null]);
            return Result;
        }
    }
}
