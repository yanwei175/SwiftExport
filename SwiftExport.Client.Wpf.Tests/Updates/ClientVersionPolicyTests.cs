using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.Apis.SystemApis;
using SwiftExport.Contracts.DTOs.Updates;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Updates;

namespace SwiftExport.Client.Wpf.Tests.Updates;

/// <summary>验证包实际版本与后端最低兼容版本策略仍一致。</summary>
[TestClass]
public sealed class ClientVersionPolicyTests
{
    /// <summary>最低版本继续阻止登录，可选更新及最新版本保持原有行为。</summary>
    [TestMethod]
    [DataRow("0.1.0", "0.1.1", "0.1.2", UpdateRequirement.Required)]
    [DataRow("0.1.1", "0.1.1", "0.1.2", UpdateRequirement.Optional)]
    [DataRow("0.1.2", "0.1.1", "0.1.2", UpdateRequirement.None)]
    public async Task InstalledVersion_DeterminesCompatibility(string current, string minimum, string latest, UpdateRequirement expected)
    {
        var service = new ClientVersionService(new ApiStub(new() { LatestVersion = latest, MinimumSupportedVersion = minimum }),
            new ApiCallRunner(NullLogger<ApiCallRunner>.Instance),
            new ClientUpdateTests.UpdateStub { CurrentVersion = current }, NullLogger<ClientVersionService>.Instance);
        var result = await service.CheckAsync();
        Assert.AreEqual(current, result.CurrentVersion);
        Assert.AreEqual(expected, result.Requirement);
        Assert.AreEqual(expected != UpdateRequirement.Required, result.CanContinue);
    }

    /// <summary>提供固定兼容策略，不访问部署服务器。</summary>
    private sealed class ApiStub(ClientVersionInfo info) : IClientVersionApi
    {
        public Task<ServiceResult<ClientVersionInfo>> GetAsync() => Task.FromResult(ServiceResult<ClientVersionInfo>.Ok(info));
    }
}
