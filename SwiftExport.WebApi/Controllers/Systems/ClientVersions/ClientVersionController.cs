using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Contracts.DTOs.Updates;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers.Systems.ClientVersions
{
    /// <summary>
    /// 客户端版本策略控制器。
    /// </summary>
    [ApiController]
    [AllowAnonymous]
    [Route("api/client-version")]
    public class ClientVersionController(IConfiguration configuration) : ControllerBase
    {
        [HttpGet]
        public ServiceResult<ClientVersionInfo> Get()
        {
            var section = configuration.GetSection("ClientVersion");
            var info = new ClientVersionInfo
            {
                LatestVersion = section["LatestVersion"] ?? "0.1.0",
                MinimumSupportedVersion = section["MinimumSupportedVersion"] ?? "0.1.0",
                DownloadUrl = section["DownloadUrl"] ?? string.Empty,
                ReleaseNotes = section["ReleaseNotes"] ?? string.Empty
            };

            return ServiceResult<ClientVersionInfo>.Ok(info);
        }
    }
}
