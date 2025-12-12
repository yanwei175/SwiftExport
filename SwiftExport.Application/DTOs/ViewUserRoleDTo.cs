using SwiftExport.Core.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.AppLayer.DTOs
{
    public class ViewUserRoleDTo  
    {
        public int ID { get; set; }
        public string UserName { get; set; }
        public string PcName { get; set; }
        public string NickName { get; set; }
        public int RoleID { get; set; }
        public string RoleName { get; set; }

    }
}
