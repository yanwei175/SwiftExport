using System.Collections.Generic;

namespace SwiftExport.View.FW.Security
{
    // ★ 全局静态权限管理器 (单例模式)
    public class PermissionManager
    {
        // 1. 单例实例，方便 XAML 里用 {x:Static} 直接绑定
        public static PermissionManager Instance { get; } = new PermissionManager();

        // 2. 使用 HashSet 存储权限码，查询速度 O(1)，性能极速！
        private HashSet<string> _permissions = new HashSet<string>();

        // 私有构造函数，防止外部 new
        private PermissionManager() { }

        // 3. 在登录成功后，调用此方法初始化权限库！
        public void Initialize(IEnumerable<string> permissions)
        {
            _permissions.Clear();
            if (permissions != null)
            {
                foreach (var p in permissions)
                {
                    _permissions.Add(p);
                }
            }
        }

        // 4. 核心鉴权方法
        public bool HasPermission(string permissionCode)
        {
            // ==========================================
            // 🚀 敏捷开发占位符：全局上帝模式！
            // ==========================================
            // 等你哪天把后端数据库、API 全做完了，只需要把下面这行代码注释掉，
            // 整个系统的权限控制瞬间真实生效！你的 XAML 连碰都不用碰！
            return true;

            // --- 下面是未来的真实逻辑 ---
            // if (string.IsNullOrWhiteSpace(permissionCode)) return true; 
            // return _permissions.Contains(permissionCode);
        }
    }
}