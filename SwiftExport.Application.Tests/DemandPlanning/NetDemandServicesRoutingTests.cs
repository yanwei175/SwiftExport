using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Interfaces.DemandPlanning;
using SwiftExport.Application.Services.DemandPlanning;
using SwiftExport.WebApi.Controllers.DemandPlanning;

namespace SwiftExport.Application.Tests.DemandPlanning;

/// <summary>验证物料 HTTP 用例进入新服务，旧计划与采购用例继续兼容，且参数和结果原样传递。</summary>
[TestClass]
public sealed class NetDemandServicesRoutingTests
{
    /// <summary>全部物料 action 必须调用新接口，不能继续进入旧混合服务。</summary>
    [TestMethod]
    public void MaterialActions_ForwardToNewService()
    {
        var material = DispatchProxy.Create<INetDemandServices, ForwardingProbe>();
        var legacy = DispatchProxy.Create<INetDemandWorkspaceService, ForwardingProbe>();
        var controller = new NetDemandWorkspaceController(material, legacy);

        foreach (var method in typeof(INetDemandServices).GetMethods())
            VerifyForwarding(controller, method, (ForwardingProbe)(object)material);

        Assert.AreEqual(0, ((ForwardingProbe)(object)legacy).CallCount);
    }

    /// <summary>尚未迁移的计划与采购 action 不能混入新物料服务。</summary>
    [TestMethod]
    public void LegacyActions_RemainOnCompatibilityService()
    {
        var material = DispatchProxy.Create<INetDemandServices, ForwardingProbe>();
        var legacy = DispatchProxy.Create<INetDemandWorkspaceService, ForwardingProbe>();
        var controller = new NetDemandWorkspaceController(material, legacy);
        var materialNames = typeof(INetDemandServices).GetMethods().Select(method => method.Name).ToHashSet();

        foreach (var method in typeof(INetDemandWorkspaceService).GetMethods().Where(method => !materialNames.Contains(method.Name)))
            VerifyForwarding(controller, method, (ForwardingProbe)(object)legacy);

        Assert.AreEqual(0, ((ForwardingProbe)(object)material).CallCount);
    }

    /// <summary>新旧服务均使用请求级注册，新服务不能因遗漏 DI 而无法被 Controller 激活。</summary>
    [TestMethod]
    public void ApplicationRegistration_RegistersBothScopedServices()
    {
        var services = new ServiceCollection();
        services.AddApplicationServices();
        var material = services.Single(item => item.ServiceType == typeof(INetDemandServices));
        var legacy = services.Single(item => item.ServiceType == typeof(INetDemandWorkspaceService));

        Assert.AreEqual(typeof(NetDemandServices), material.ImplementationType);
        Assert.AreEqual(ServiceLifetime.Scoped, material.Lifetime);
        Assert.AreEqual(typeof(NetDemandWorkspaceService), legacy.ImplementationType);
        Assert.AreEqual(ServiceLifetime.Scoped, legacy.Lifetime);
    }

    /// <summary>真实调用 Controller 并检查目标、所有参数及返回任务，避免只搜索源码文本。</summary>
    private static void VerifyForwarding(NetDemandWorkspaceController controller, MethodInfo method, ForwardingProbe probe)
    {
        using var cancellation = new CancellationTokenSource();
        var arguments = method.GetParameters().Select(parameter =>
            parameter.ParameterType == typeof(CancellationToken) ? (object)cancellation.Token :
            parameter.ParameterType == typeof(Guid) ? Guid.NewGuid() :
            parameter.ParameterType == typeof(string) ? "SO" :
            Activator.CreateInstance(parameter.ParameterType)).ToArray();
        var before = probe.CallCount;
        var result = typeof(NetDemandWorkspaceController).GetMethod(method.Name)!.Invoke(controller, arguments);

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
