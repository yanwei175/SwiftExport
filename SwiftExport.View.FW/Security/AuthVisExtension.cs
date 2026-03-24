using SwiftExport.View.FW.Resources.Controls.Converters;
using System;
using System.Windows.Data;
using System.Windows.Markup;

namespace SwiftExport.View.FW.Security
{
    // ★ 定义一个专门用于权限控制的语法糖
    public class AuthVisExtension : MarkupExtension
    {
        // 接收你要传入的权限码
        public string Code { get; set; }

        // 构造函数：允许你在 XAML 里直接传参，不用写 Code="..."
        public AuthVisExtension(string code)
        {
            Code = code;
        }

        // 核心魔法：系统在解析 XAML 时会调用这个方法
        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            // 在 C# 里动态帮你生成那长长的一坨 Binding！
            var binding = new Binding
            {
                Source = PermissionManager.Instance, // 自动绑定到你的全局单例
                Converter = new PermissionToVisibilityConverter(), // 自动挂载转换器
                ConverterParameter = Code // 把你传进来的权限码塞进去
            };

            // 返回组装好的 Binding 给 UI
            return binding.ProvideValue(serviceProvider);
        }
    }
}