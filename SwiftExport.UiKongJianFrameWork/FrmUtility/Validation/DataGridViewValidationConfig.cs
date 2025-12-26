using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.UiKongJianFrameWork.FrmUtility
{
    public class DataGridViewValidationConfig
    {
        // 各种验证规则的列名集合
        public HashSet<string> NotNullColumns { get; } = new HashSet<string>();
        public HashSet<string> DateColumns { get; } = new HashSet<string>();
        public HashSet<string> NumericColumns { get; } = new HashSet<string>();
        public HashSet<string> UniqueColumns { get; } = new HashSet<string>();
        public HashSet<string> UcaseColumns { get; } = new HashSet<string>();
        // 可以在这里扩展更多，比如正则表达式、范围验证等
    }


}
