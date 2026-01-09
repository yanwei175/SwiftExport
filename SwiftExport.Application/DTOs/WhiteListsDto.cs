using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.AppLayer.DTOs
{
    public class WhiteListsDto : BaseDto
    {
        [Column("Even")]
        public string Even { get; set; }

        [Column("Status")]
        public string 状态 { get; set; }

        [Column("ExcelWhiteList")]
        public string Excel白名单 { get; set; }

        [Browsable(false)]
        public string SearchKey => $"{Excel白名单}".ToLower();
    }
}
