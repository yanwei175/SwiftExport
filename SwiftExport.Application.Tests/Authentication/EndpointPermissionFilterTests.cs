using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Models.Auth.Permissions.Evaluation;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.WebApi.Authentication;

namespace SwiftExport.Application.Tests.Authentication;

/// <summary>执行端点权限过滤器，验证冻结矩阵的单键与批量操作不会绕过服务端鉴权。</summary>
[TestClass]
public sealed class EndpointPermissionFilterTests
{
    private static readonly Lazy<IReadOnlyDictionary<string, ControllerActionDescriptor>> MvcActions = new(() =>
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers().AddApplicationPart(typeof(EndpointPermissionFilter).Assembly);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>()
            .GroupBy(item => $"{item.ControllerTypeInfo.Name}.{item.MethodInfo.Name}", StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
    });

    /// <summary>每个单键 Action 在无权限时拒绝，只授予该键时才进入业务 Action。</summary>
    [TestMethod]
    public async Task EveryRegisteredSingleKey_RequiresItsExactPermission()
    {
        var routes = ReadMap("SingleKeys");
        Assert.AreEqual(205, routes.Count);
        foreach (var (action, key) in routes)
        {
            var denied = await ExecuteAsync(action, []);
            Assert.IsFalse(denied.Continued, action);
            Assert.AreEqual(ServiceErrorCodes.Forbidden, denied.ErrorCode, action);
            CollectionAssert.AreEqual(new[] { key }, denied.RequestedKeys, action);

            var allowed = await ExecuteAsync(action, [key]);
            Assert.IsTrue(allowed.Continued, action);
            Assert.IsNull(allowed.ErrorCode, action);
            CollectionAssert.AreEqual(new[] { key }, allowed.RequestedKeys, action);
        }
    }

    /// <summary>每个批量 Action 对实际出现的创建、编辑、删除独立检查，空操作一律拒绝。</summary>
    [TestMethod]
    public async Task EveryBatchAction_RequiresEachActualOperationAndRejectsEmptyBatch()
    {
        var routes = ReadMap("BatchPrefixes");
        Assert.AreEqual(16, routes.Count);
        foreach (var (action, prefix) in routes)
        {
            foreach (var operation in new[] { "Create", "Edit", "Delete" })
            {
                var request = BatchRequest.For(operation);
                var expectedKey = $"{prefix}.{operation}";
                var denied = await ExecuteAsync(action, [], request);
                Assert.IsFalse(denied.Continued, $"{action}/{operation}");
                Assert.AreEqual(ServiceErrorCodes.Forbidden, denied.ErrorCode, $"{action}/{operation}");
                CollectionAssert.AreEqual(new[] { expectedKey }, denied.RequestedKeys, $"{action}/{operation}");

                var allowed = await ExecuteAsync(action, [expectedKey], request);
                Assert.IsTrue(allowed.Continued, $"{action}/{operation}");
                CollectionAssert.AreEqual(new[] { expectedKey }, allowed.RequestedKeys, $"{action}/{operation}");
            }

            var empty = await ExecuteAsync(action, [
                $"{prefix}.Create", $"{prefix}.Edit", $"{prefix}.Delete"], new BatchRequest());
            Assert.IsFalse(empty.Continued, action);
            Assert.AreEqual(ServiceErrorCodes.Validation, empty.ErrorCode, action);
            Assert.HasCount(0, empty.RequestedKeys, action);
        }
    }

    /// <summary>未登记 Action 和采购候选例外保持拒绝优先，不能用其他业务权限代替。</summary>
    [TestMethod]
    public async Task UnknownAndCandidateActions_FailClosed()
    {
        var unknown = await ExecuteAsync("UnknownController.Search", ["System.Users.Manage"]);
        Assert.IsFalse(unknown.Continued);
        Assert.AreEqual(ServiceErrorCodes.Forbidden, unknown.ErrorCode);

        const string candidate = "WarehouseSitesController.SearchCandidatesAsync";
        var denied = await ExecuteAsync(candidate, ["Inventory.Warehouse.View"]);
        Assert.IsFalse(denied.Continued);
        Assert.AreEqual(ServiceErrorCodes.Forbidden, denied.ErrorCode);
        CollectionAssert.AreEqual(new[] { "Procurement.Order.Create", "Procurement.Order.Confirm" }, denied.RequestedKeys);
        Assert.IsTrue((await ExecuteAsync(candidate, ["Procurement.Order.Create"])).Continued);
        Assert.IsTrue((await ExecuteAsync(candidate, ["Procurement.Order.Confirm"])).Continued);
    }

    /// <summary>用真实 ActionExecutingContext 执行过滤器，并记录实际检查的权限键。</summary>
    private static async Task<Outcome> ExecuteAsync(string action, IReadOnlyCollection<string> allowed, object? request = null)
    {
        var descriptor = MvcActions.Value.TryGetValue(action, out var actual)
            ? actual
            : new ControllerActionDescriptor
            {
                ControllerTypeInfo = typeof(EndpointPermissionFilterTests).GetTypeInfo(),
                MethodInfo = typeof(EndpointPermissionFilterTests).GetMethod(nameof(UnknownAction))!,
                RouteValues = new Dictionary<string, string?>(StringComparer.Ordinal)
            };
        var actionContext = new ActionContext(new DefaultHttpContext(), new RouteData(), descriptor);
        var filters = new List<IFilterMetadata>();
        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (request is not null) arguments["request"] = request;
        var context = new ActionExecutingContext(actionContext, filters, arguments, new object());
        var evaluator = new PermissionStub(allowed);
        var filter = new EndpointPermissionFilter(new UserStub(), evaluator);
        var continued = false;
        await filter.OnActionExecutionAsync(context, () =>
        {
            continued = true;
            return Task.FromResult(new ActionExecutedContext(actionContext, filters, new object()));
        });
        var error = (context.Result as ObjectResult)?.Value as ServiceResult<object>;
        return new Outcome(continued, error?.Error?.Code, evaluator.RequestedKeys.ToArray());
    }

    /// <summary>仅为未知 Action 拒绝分支提供不在生产映射中的方法描述符。</summary>
    public void UnknownAction() { }

    /// <summary>读取本次冻结的生产映射，随后对每项执行真实过滤器逻辑。</summary>
    private static IReadOnlyDictionary<string, string> ReadMap(string fieldName)
    {
        var field = typeof(EndpointPermissionFilter).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(field);
        var map = field.GetValue(null) as Dictionary<string, string>;
        Assert.IsNotNull(map);
        return map;
    }

    /// <summary>记录过滤器是否进入业务 Action、拒绝代码及实际检查的权限键。</summary>
    private sealed record Outcome(bool Continued, string? ErrorCode, string[] RequestedKeys);

    /// <summary>只模拟存在的操作集合，避免把未提交操作错误地算作所需权限。</summary>
    private sealed class BatchRequest
    {
        public object[] CreateItems { get; set; } = [];
        public object[] UpdateItems { get; set; } = [];
        public object[] DeleteItems { get; set; } = [];

        /// <summary>只构造指定一种批量操作，验证不同写权限不会互相替代。</summary>
        public static BatchRequest For(string operation) => operation switch
        {
            "Create" => new BatchRequest { CreateItems = [new object()] },
            "Edit" => new BatchRequest { UpdateItems = [new object()] },
            "Delete" => new BatchRequest { DeleteItems = [new object()] },
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
    }

    /// <summary>测试身份始终已登录，以便精确验证业务权限分支。</summary>
    private sealed class UserStub : ICurrentUser
    {
        /// <summary>提供测试身份显示名，不参与授权决策。</summary>
        public string UserName => "permission-test";
        /// <summary>提供当前测试用户标识。</summary>
        public Guid? UserId { get; } = Guid.NewGuid();
        /// <summary>将测试会话固定为有效登录。</summary>
        public bool IsAuthenticated => true;
    }

    /// <summary>按显式键答复权限检查，并记录过滤器实际请求的每个键。</summary>
    private sealed class PermissionStub(IReadOnlyCollection<string> allowed) : IPermissionEvaluationService
    {
        /// <summary>记录实际请求的键，以验证端点和权限目录的映射。</summary>
        public List<string> RequestedKeys { get; } = [];

        /// <summary>本测试只检查权限键，不生成用户投影。</summary>
        public Task<UserPermissionProjection?> GetProjectionAsync(Guid userId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        /// <summary>只允许显式传入的权限键，并记录每次判断。</summary>
        public Task<bool> HasPermissionAsync(Guid userId, string permissionKey, CancellationToken cancellationToken = default)
        {
            RequestedKeys.Add(permissionKey);
            return Task.FromResult(allowed.Contains(permissionKey, StringComparer.Ordinal));
        }
    }
}
