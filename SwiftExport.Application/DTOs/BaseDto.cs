using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.AppLayer.DTOs
{
    public enum RowState
    {
        无变化,
        新增,
        修改,
        删除
    }
    public class BaseDto
    {

        public int ID { get; set; }
        public short 是否删除 { get; set; }
        public DateTime 创建时间 { get; set; }
        public DateTime 修改时间 { get; set; }
        public RowState 视图状态 { get; set; } = RowState.无变化;
    }
}
