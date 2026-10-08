using System;
using System.IO;
using System.ComponentModel;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;

namespace SwiftExport.Client.VSTO.Diagnostics
{
    /// <summary>记录固定事件、异常类型及诊断码，不记录请求正文、密码、令牌或异常正文。</summary>
    public sealed class AddInLog
    {
        private readonly object gate = new object();
        /// <summary>尽力追加日志；日志写入失败不能中断 Excel。</summary>
        public void Write(string operation, Exception exception = null)
        {
            try
            {
                lock (gate)
                {
                    var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SwiftExport", "Vsto", "Logs");
                    Directory.CreateDirectory(directory);
                    var file = Path.Combine(directory, "addin.log");
                    if (File.Exists(file) && new FileInfo(file).Length > 1024 * 1024) File.Delete(file);
                    var details = FormatException(exception);
                    // Excel 的运行环境可能不同于独立测试进程，失败时记录当下设置而不修改它们。
                    if (exception is HttpRequestException) details += " " + FormatNetworkEnvironment();
                    File.AppendAllText(file, DateTime.UtcNow.ToString("O") + " " + operation + " " + details + Environment.NewLine);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        /// <summary>记录实际加载的服务器主机与端口；不输出路径、查询参数或用户信息。</summary>
        public void WriteApiEndpoint(Uri address)
        {
            Write("ApiEndpoint " + address.Scheme + "://" + address.IdnHost + ":" + address.Port);
        }

        /// <summary>只保留有限层数的类型及错误码，避免异常消息携带凭据或服务器正文。</summary>
        internal static string FormatException(Exception exception)
        {
            var result = new StringBuilder();
            for (var depth = 0; exception != null && depth < 8; depth++, exception = exception.InnerException)
            {
                if (depth != 0) result.Append(" -> ");
                result.Append(exception.GetType().FullName).Append(" HResult=0x").Append(exception.HResult.ToString("X8"));
                var web = exception as WebException;
                if (web != null) result.Append(" WebStatus=").Append(web.Status);
                var socket = exception as SocketException;
                if (socket != null) result.Append(" SocketError=").Append(socket.SocketErrorCode);
                var native = exception as Win32Exception;
                if (native != null) result.Append(" NativeError=").Append(native.NativeErrorCode);
            }
            return result.ToString();
        }

        /// <summary>读取 Excel AppDomain 当下的 TLS 设置，仅用于诊断，不覆盖宿主全局设置。</summary>
        private static string FormatNetworkEnvironment()
        {
            bool strongCrypto, systemTls;
            var hasStrongCrypto = AppContext.TryGetSwitch("Switch.System.Net.DontEnableSchUseStrongCrypto", out strongCrypto);
            var hasSystemTls = AppContext.TryGetSwitch("Switch.System.Net.DontEnableSystemDefaultTlsVersions", out systemTls);
            return "SecurityProtocol=" + ServicePointManager.SecurityProtocol
                + " RevocationCheck=" + ServicePointManager.CheckCertificateRevocationList
                + " DontEnableSchUseStrongCrypto=" + (hasStrongCrypto ? strongCrypto.ToString() : "Unset")
                + " DontEnableSystemDefaultTlsVersions=" + (hasSystemTls ? systemTls.ToString() : "Unset");
        }
    }
}
