namespace SwiftExport.View.FW
{
    /// <summary>
    /// 前端运行时全局配置选项。
    /// 在 App.xaml.cs 启动阶段赋值，影响 HTTP 超时、预加载等行为。
    /// </summary>
    public static class AppRuntimeOptions
    {
        /// <summary>
        /// 离线模式开关。
        /// true  = 启动时不依赖后端联调，API 请求超时设为 2 秒，预加载全部跳过。
        /// false = 正常联调模式，超时 30 秒。
        /// </summary>
        public static bool DisableRemoteApi { get; set; } = false;
    }
}
