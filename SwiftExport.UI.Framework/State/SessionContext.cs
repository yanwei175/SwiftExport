using SwiftExport.AppLayer.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.UI.Framework.State
{
    public class SessionContext
    {
        // 全局静态变量，存放当前登录用户
        public static ViewUserRoleDTo CurrentUser { get; private set; }

        // 是否已登录
        public static bool IsLogged => CurrentUser != null;

        // 登录方法 (由 LoginFrm 调用)
        public static void SetUser(ViewUserRoleDTo user)
        {
            CurrentUser = user;
        }

        // 登出方法
        public static void Logout()
        {
            CurrentUser = null;
        }
    }
}
