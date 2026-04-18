using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.Auth
{
    public class LoginResultDto
    {
        public int UserId { get; set; }
        public string UserName { get; set; }
        public string Token { get; set; }

        // ★ 核心：前端只认这个一维数组！后端负责把复杂的树形结构“拍平”！
        public List<string> Permissions { get; set; }
    }
}
