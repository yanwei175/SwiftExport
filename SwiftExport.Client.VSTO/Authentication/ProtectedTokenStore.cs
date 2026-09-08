using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using SwiftExport.Client.VSTO.Diagnostics;

namespace SwiftExport.Client.VSTO.Authentication
{
    /// <summary>按服务器隔离 DPAPI 缓存，跨 Excel 进程互斥并原子替换文件。</summary>
    public sealed class ProtectedTokenStore : ITokenStore
    {
        private readonly string file;
        private readonly string mutexName;
        private readonly AddInLog log;
        /// <summary>从规范化服务器地址计算缓存命名，不在路径中暴露地址。</summary>
        public ProtectedTokenStore(Uri server, AddInLog log)
        {
            this.log = log;
            string key;
            using (var hash = SHA256.Create()) key = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(server.AbsoluteUri.TrimEnd('/')))).Replace("-", "");
            file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SwiftExport", "Vsto", key, "token.cache");
            mutexName = "Local\\SwiftExport.Vsto.Token." + key;
        }
        /// <inheritdoc />
        public string Read()
        {
            try { return Locked(() => File.Exists(file) ? Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(file), null, DataProtectionScope.CurrentUser)) : null); }
            catch (Exception ex) when (ex is IOException || ex is CryptographicException || ex is UnauthorizedAccessException)
            { log.Write("TokenReadFailure", ex); return null; }
        }
        /// <inheritdoc />
        public void Write(string token)
        {
            Locked(() =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllBytes(temporary, ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser));
                    if (File.Exists(file)) File.Replace(temporary, file, null); else File.Move(temporary, file);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
                return true;
            });
        }
        /// <inheritdoc />
        public void Clear() { Locked(() => { if (File.Exists(file)) File.Delete(file); return true; }); }
        // 命名互斥锁只覆盖短文件操作，异常退出遗留的互斥锁也必须释放。
        private T Locked<T>(Func<T> action)
        {
            using (var mutex = new Mutex(false, mutexName))
            {
                var owned = false;
                try
                {
                    try { owned = mutex.WaitOne(TimeSpan.FromSeconds(2)); }
                    catch (AbandonedMutexException) { owned = true; }
                    if (!owned) throw new IOException("登录缓存正在使用。");
                    return action();
                }
                finally { if (owned) mutex.ReleaseMutex(); }
            }
        }
    }
}
