using System.Threading.Tasks;
using SwiftExport.Contracts.DTOs.Auth;

namespace SwiftExport.Client.VSTO.Authentication
{
    /// <summary>隔离候选身份请求，不修改当前账号的授权头或会话。</summary>
    public interface IAuthGateway
    {
        /// <summary>验证密码并返回候选令牌。</summary>
        Task<string> LoginAsync(string userName, string password);
        /// <summary>仅用指定候选令牌取得用户和权限。</summary>
        Task<MeDto> GetMeAsync(string token);
    }
}
