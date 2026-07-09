using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.Orders;

[Table("FollowUpLog")]
public class FollowUpLog : BaseEntity
{
    //跟进的目标行Id,可能是采购订单行，也可能是净需求行，甚至其他类型的行（如生产订单行）。通过 RefLineId 和 RefLineType 来关联具体的业务行。
    public Guid RefLineId { get; set; }
    public string? NoteContent { get; set; }
}
