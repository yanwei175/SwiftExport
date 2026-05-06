using SwiftExport.Core.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Core.Entities.Orders
{
    public class FollowUpLog : BaseEntity
    {
        public int RefLineId { get; set; }
        public string? NoteContent { get; set; }
    }
}
