using SwiftExport.Core.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Core.EntitieView
{
    public class ViewUserRole: BaseEntity
    {
        public string UserName { get; set; }
        public string PcName { get; set; }
        public string Password { get; set; }
        public string NickName { get; set; }
        public int RoleID { get; set; }
        public string RoleName { get; set; }
    }
}
