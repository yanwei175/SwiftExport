using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;

namespace SwiftExport.AppLayer.DTOs
{
    public enum RowState
    {
        正常,
        新增,
        修改,
        删除
    }
    public class BaseDto 
    {
        // 关键：这个静态变量留着，配合你的 SilentUpdate 扩展方法
        // 虽然 DTO 现在是纯净的，但在某些反射逻辑或拦截器里它依然有用
        [Browsable(false)]
        public static bool Loading { get; set; } = false;

        public int ID { get; set; }
        public short 是否删除 { get; set; }
        public DateTime 创建时间 { get; set; }
        public DateTime 修改时间 { get; set; }
        public RowState 视图状态 { get; set; } = RowState.正常;

    }
}
