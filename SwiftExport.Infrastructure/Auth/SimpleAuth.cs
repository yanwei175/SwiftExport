using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Infrastructure.Auth
{
    public static class SimpleAuth
    {
        private static Dictionary<string, string> _users = new Dictionary<string, string>
        {
            { "admin", "123456" }
        };

        public static bool Login(string username, string password)
        {
            return _users.ContainsKey(username) && _users[username] == password;
        }
    }

}
