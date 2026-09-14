using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Interfaces.DemandPlanning;
using SwiftExport.Application.Services.DemandPlanning;
using SwiftExport.WebApi.Controllers.DemandPlanning;

namespace SwiftExport.Application.Tests.DemandPlanning;

/// <summary>验证物料管理 HTTP 用例完整转发，且旧计划边界已经退出。</summary>
[TestClass]
public sealed class NetDemandManagerServiceRoutingTests
{
    /// <summary>全部物料 action 必须调用新接口，不能继续进入旧混合服务。</summary>
    [TestMethod]
    public void MaterialActions_ForwardToNewService()
    {
        var material = DispatchProxy.Create<INetDemandManagerService, ForwardingProbe>();
        var controller = new NetDemandManagerController(material);

        foreach (var method in typeof(INetDemandManagerService).GetMethods())
            VerifyForwarding(controller, method, (ForwardingProbe)(object)material);

    }

    /// <summary>已退出的旧计划 action 不能重新进入当前物料事实边界。</summary>
    [TestMethod]
    public void LegacyActions_AreRemoved()
    {
        foreach (var name in new[] { "ReplacePlanAsync", "ApplyDefaultSupplierAsync", "BatchAssignSupplierAsync", "GetDetailAsync" })
        {
            Assert.IsNull(typeof(NetDemandManagerController).GetMethod(name));
            Assert.IsNull(typeof(INetDemandManagerService).GetMethod(name));
        }
    }

    /// <summary>仅注册请求级 Manager 服务，不遗留已退出的计划服务。</summary>
    [TestMethod]
    public void ApplicationRegistration_RegistersManagerOnly()
    {
        var services = new ServiceCollection();
        services.AddApplicationServices();
        var material = services.Single(item => item.ServiceType == typeof(INetDemandManagerService));

        Assert.AreEqual(typeof(NetDemandManagerService), material.ImplementationType);
        Assert.AreEqual(ServiceLifetime.Scoped, material.Lifetime);
        Assert.IsFalse(services.Any(item => item.ServiceType.Name == "INetDemandWorkspaceService"));
    }

    /// <summary>真实调用 Controller 并检查目标、所有参数及返回任务，避免只搜索源码文本。</summary>
    private static void VerifyForwarding(NetDemandManagerController controller, MethodInfo method, ForwardingProbe probe)
    {
        using var cancellation = new CancellationTokenSource();
        var arguments = method.GetParameters().Select(parameter =>
            parameter.ParameterType == typeof(CancellationToken) ? (object)cancellation.Token :
            parameter.ParameterType == typeof(Guid) ? Guid.NewGuid() :
            parameter.ParameterType == typeof(string) ? "SO" :
            Activator.CreateInstance(parameter.ParameterType)).ToArray();
        var before = probe.CallCount;
        var result = typeof(NetDemandManagerController).GetMethod(method.Name)!.Invoke(controller, arguments);

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
