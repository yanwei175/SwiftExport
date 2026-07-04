namespace SwiftExport.Contracts.DTOs.Updates
{
    /// <summary>
    /// 客户端版本策略信息，用于启动时判断是否需要更新。
    /// </summary>
    public class ClientVersionInfo
    {
        public string LatestVersion { get; set; } = string.Empty;

        public string MinimumSupportedVersion { get; set; } = string.Empty;

        public string DownloadUrl { get; set; } = string.Empty;

        public string ReleaseNotes { get; set; } = string.Empty;
    }
}
