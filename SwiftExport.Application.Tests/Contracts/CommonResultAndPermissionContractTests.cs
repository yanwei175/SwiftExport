using System.Reflection;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Tests.Contracts;

/// <summary>验证通用结果、错误与权限基础契约的向后兼容和序列化形状。</summary>
[TestClass]
public sealed class CommonResultAndPermissionContractTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    /// <summary>旧成功和失败工厂保持原签名与行为，旧失败不强制产生结构化错误。</summary>
    [TestMethod]
    public void Legacy_service_result_factories_remain_compatible()
    {
        var success = ServiceResult<int>.Ok(7, "完成");
        var failure = ServiceResult<int>.Fail("失败");

        Assert.IsTrue(success.Success);
        Assert.AreEqual(7, success.Data);
        Assert.AreEqual("完成", success.Message);
        Assert.IsFalse(failure.Success);
        Assert.AreEqual("失败", failure.Message);
        Assert.IsNull(failure.Error);

        var legacyPayload = JsonSerializer.Deserialize<ServiceResult<int>>(
            "{\"success\":false,\"message\":\"旧服务失败\",\"data\":0}", WebJson);
        Assert.IsNotNull(legacyPayload);
        Assert.AreEqual("旧服务失败", legacyPayload.Message);
        Assert.IsNull(legacyPayload.Error);
    }

    /// <summary>新失败工厂同时保持顶层消息，并序列化稳定代码、消息和字段路径。</summary>
    [TestMethod]
    public void Structured_service_error_serializes_with_legacy_fields()
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
            nameof(ServiceErrorCodes.Unexpected)
        }, commonNames);

        CollectionAssert.AreEqual(new[] { nameof(ProcurementErrorCodes.BomBasisChanged) },
            typeof(ProcurementErrorCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Select(field => field.Name).ToArray());
    }

    /// <summary>角色与权限分别投影，管理员保留角色身份并获得全部稳定权限。</summary>
    [TestMethod]
    public void Role_and_permission_projection_are_distinct()
    {
        var assigned = new[] { "Buyer", PermissionKeys.ProcurementOrderRead, "Admin" };

        CollectionAssert.AreEquivalent(new[] { "Buyer", "Admin" },
            PermissionKeys.ExtractRoles(assigned));
        CollectionAssert.AreEqual(PermissionKeys.All.ToArray(),
            PermissionKeys.ExtractPermissions(assigned));
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

    /// <summary>采购命令事务必须继续实时查询权限，不得依赖 /me 的客户端权限快照。</summary>
    [TestMethod]
    public void Procurement_commands_keep_realtime_server_permission_check()
    {
        var source = File.ReadAllText(Path.Combine(Root(), "SwiftExport.Application", "Services",
            "Procurement", "OrderManagement", "ProcurementOrderTransaction.cs"));

        StringAssert.Contains(source, "permissions.HasPermissionAsync(user.UserId.Value, ResolvePermissionKey(operation), ct)");
        Assert.IsFalse(source.Contains("MeDto", StringComparison.Ordinal));
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
