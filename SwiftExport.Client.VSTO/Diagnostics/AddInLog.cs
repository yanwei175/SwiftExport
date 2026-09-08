using System;
using System.IO;

namespace SwiftExport.Client.VSTO.Diagnostics
{
    /// <summary>记录固定事件名与异常类型，不记录请求、密码、令牌或异常正文。</summary>
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
                    File.AppendAllText(file, DateTime.UtcNow.ToString("O") + " " + operation + " " + exception?.GetType().Name + Environment.NewLine);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
