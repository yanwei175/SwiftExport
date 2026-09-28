using System.Reflection;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Services.Auth.Permissions.Evaluation;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Auth.Permissions;
using SwiftExport.Core.Models.Auth;

namespace SwiftExport.Application.Tests.Contracts;

/// <summary>验证通用结果、错误与权限基础契约的错误分类和序列化形状。</summary>
[TestClass]
public sealed class CommonResultAndPermissionContractTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    /// <summary>成功工厂保留数据，单消息失败工厂明确归类为业务阻断。</summary>
    [TestMethod]
    public void Service_result_factories_produce_structured_failures()
    {
        var success = ServiceResult<int>.Ok(7, "完成");
        var failure = ServiceResult<int>.Fail("失败");

        Assert.IsTrue(success.Success);
        Assert.AreEqual(7, success.Data);
        Assert.AreEqual("完成", success.Message);
        Assert.IsFalse(failure.Success);
        Assert.AreEqual("失败", failure.Message);
        Assert.AreEqual(ServiceErrorCodes.BusinessBlocked, failure.Error.Code);
    }

    /// <summary>新失败工厂同时保持顶层消息，并序列化稳定代码、消息和字段路径。</summary>
    [TestMethod]
    public void Structured_service_error_serializes_with_shared_fields()
    {
        var result = ServiceResult<object>.Fail(
            ServiceErrorCodes.Validation, "名称不能为空。", "Name");
        var json = JsonSerializer.Serialize(result, WebJson);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.IsFalse(root.GetProperty("success").GetBoolean());
        Assert.AreEqual("名称不能为空。", root.GetProperty("message").GetString());
        Assert.AreEqual(ServiceErrorCodes.Validation,
            root.GetProperty("error").GetProperty("code").GetString());
        Assert.AreEqual("Name", root.GetProperty("error").GetProperty("field").GetString());
    }

    /// <summary>公共错误只含跨模块类别，采购错误只保留采购专属分类。</summary>
    [TestMethod]
    public void Common_and_procurement_error_code_boundaries_are_separate()
    {
        var commonNames = typeof(ServiceErrorCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => field.Name)
            .ToArray();
        CollectionAssert.AreEquivalent(new[]
        {
            nameof(ServiceErrorCodes.Unauthenticated), nameof(ServiceErrorCodes.Forbidden),
            nameof(ServiceErrorCodes.Validation), nameof(ServiceErrorCodes.VersionConflict),
            nameof(ServiceErrorCodes.CommandConflict), nameof(ServiceErrorCodes.BusinessBlocked),
            nameof(ServiceErrorCodes.Unexpected), nameof(ServiceErrorCodes.NotFound),
            nameof(ServiceErrorCodes.StateBlocked), nameof(ServiceErrorCodes.CommandBusy),
            nameof(ServiceErrorCodes.RateLimited), nameof(ServiceErrorCodes.ServiceUnavailable),
            nameof(ServiceErrorCodes.OutcomeUnknown)
        }, commonNames);

        CollectionAssert.AreEqual(new[] { nameof(ProcurementErrorCodes.BomBasisChanged) },
            typeof(ProcurementErrorCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Select(field => field.Name).ToArray());
    }

    /// <summary>角色代码与权限分别投影，系统管理员获得完整目录且显示名不决定授权。</summary>
    [TestMethod]
    public async Task Role_and_permission_projection_are_distinct()
    {
        var userId = Guid.NewGuid();
        var evaluator = new PermissionEvaluationService(new PermissionFactsStub(new UserPermissionFacts
        {
            UserId = userId, IsActive = true, IsAdmin = true,
            RoleCodes = [SystemRoleCodes.SystemAdmin], RoleNames = ["重命名的管理员"],
            PermissionKeys = [PermissionKeys.ProcurementOrderRead]
        }));

        var projection = await evaluator.GetProjectionAsync(userId);
        Assert.IsNotNull(projection);
        CollectionAssert.AreEquivalent(new[] { SystemRoleCodes.SystemAdmin }, projection.RoleCodes.ToArray());
        CollectionAssert.AreEquivalent(PermissionCatalog.All.Select(item => item.PermissionKey).ToArray(), projection.Permissions.ToArray());
        Assert.IsFalse(await evaluator.HasPermissionAsync(userId, "Unknown.Permission"));
    }

    /// <summary>仅提供当前用户的角色与权限事实，验证评估服务的真实投影边界。</summary>
    private sealed class PermissionFactsStub(UserPermissionFacts facts) : IUserPermissionRepository
    {
        public Task<UserPermissionFacts?> GetPermissionFactsAsync(Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult<UserPermissionFacts?>(userId == facts.UserId ? facts : null);

        public Task<IReadOnlyList<UserSummaryDto>> GetActiveUsersAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<UserSummaryDto>>([]);
    }

    /// <summary>当前用户 JSON 同时公开角色和权限集合，且两者均有稳定空集合默认值。</summary>
    [TestMethod]
    public void Me_contract_serializes_roles_and_permissions_separately()
    {
        var dto = new MeDto
        {
            UserId = "user-1",
            UserName = "buyer",
            Roles = new List<string> { "Buyer" },
            Permissions = new List<string> { PermissionKeys.ProcurementOrderRead }
        };
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(dto, WebJson));

        Assert.AreEqual("Buyer", document.RootElement.GetProperty("roles")[0].GetString());
        Assert.AreEqual(PermissionKeys.ProcurementOrderRead,
            document.RootElement.GetProperty("permissions")[0].GetString());
        Assert.IsNotNull(new MeDto().Roles);
        Assert.IsNotNull(new MeDto().Permissions);
    }

    /// <summary>采购读写服务必须继续实时查询权限，不得依赖 /me 的客户端权限快照。</summary>
    [TestMethod]
    public void Procurement_commands_keep_realtime_server_permission_check()
    {
        var source = File.ReadAllText(Path.Combine(Root(), "SwiftExport.Application", "Services",
            "Procurement", "PurchaseOrders", "Support", "ProcurementOrderReadSupport.cs"));

        StringAssert.Contains(source, "permissions.HasPermissionAsync(user.UserId.Value, ResolvePermissionKey(operation), ct)");
        Assert.IsFalse(source.Contains("MeDto", StringComparison.Ordinal));
        var commands = File.ReadAllText(Path.Combine(Root(), "SwiftExport.Application", "Services",
            "Procurement", "PurchaseOrders", "Support", "ProcurementOrderCommandSupport.cs"));
        StringAssert.Contains(commands, "permissions.HasPermissionAsync(actor, key, ct)");
    }

    private static string Root()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
                {
                    return directory.FullName;
                }
            }
        }

        Assert.Fail("无法定位仓库根目录。");
        return string.Empty;
    }
}
