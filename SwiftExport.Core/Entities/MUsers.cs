using SwiftExport.Core.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Core.Entities
{
    public class MUsers : BaseEntity
    {
        public string Even { get; set; }
        public string Status { get; set; }
        public string UserName { get; set; }
        public string PcName { get; set; }
        public string NickName { get; set; }
        public string Password { get; set; }
        public string Email { get; set; }
        public string PhoneNumber { get; set; }

    }
}
