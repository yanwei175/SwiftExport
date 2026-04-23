using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.View.FW.DI
{
    public static class IocProvider
    {
        // 这是一个空的委托，由主项目来负责给它赋值
        public static Func<Type, object> GetServiceFunc { get; set; }

        public static T Get<T>() => (T)GetServiceFunc?.Invoke(typeof(T));
    }
}
