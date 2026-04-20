using SwiftExport.View.FW.Mvvm;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.View.FW.DictViews.Models
{
    public class DictDataModel : BaseLineModel, IDataErrorInfo
    {
        public override void LoadFromDto<Tdto>(Tdto dto, bool 抑制清理脏数据 = false)
        {
            throw new NotImplementedException();
        }
    }
}
