using System;

namespace SwiftExport.Client.VSTO.Excel.Writing.OrderExport
{
    /// <summary>采购模板的固定坐标和动态尾行，供格式服务及后续填充共享。</summary>
    public sealed class PurchaseOrderTemplateLayout
    {
        /// <summary>每页重复打印的明细标题行。</summary>
        public const int HeaderRow = 8;
        /// <summary>首条明细行；零明细时该位置用作合计行。</summary>
        public const int FirstDetailRow = 9;
        public int DetailCount { get; }
        public int TotalRow { get; }
        /// <summary>优惠及提示区域占用的行数，不参与明细合计。</summary>
        public int NoteRowCount { get; }
        public int SignatureRow { get; }
        public string PrintArea => "$A$1:$K$" + SignatureRow;

        /// <summary>根据实际明细数定位合计、签字和打印末行；零行模板直接在第 9 行显示合计。</summary>
        public PurchaseOrderTemplateLayout(int detailCount, int noteRowCount = 0)
        {
            if (noteRowCount < 0 || noteRowCount > 1048576 - FirstDetailRow - 1)
                throw new ArgumentOutOfRangeException(nameof(noteRowCount));
            if (detailCount < 0 || detailCount > 1048576 - FirstDetailRow - 1 - noteRowCount)
                throw new ArgumentOutOfRangeException(nameof(detailCount), "明细数量超出采购模板支持的工作表范围。");
            DetailCount = detailCount;
            TotalRow = FirstDetailRow + detailCount;
            NoteRowCount = noteRowCount;
            SignatureRow = TotalRow + noteRowCount + 1;
        }
    }
}
