namespace SwiftExport.Client.VSTO.Authentication
{
    /// <summary>持久化当前服务器的访问令牌，不保存密码。</summary>
    public interface ITokenStore
    {
        /// <summary>读取令牌；不存在或无法解密时返回空值。</summary>
        string Read();
        /// <summary>原子替换加密令牌。</summary>
        void Write(string token);
        /// <summary>清除本机插件缓存。</summary>
        void Clear();
    }
}
