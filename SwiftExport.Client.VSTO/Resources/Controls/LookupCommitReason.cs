using System;
using System.Collections.Generic;
using System.Linq;
namespace SwiftExport.Client.VSTO.Resources.Controls
{

    /// <summary>说明选择来自键盘、鼠标、精确匹配或程序赋值。</summary>
    public enum LookupCommitReason
    {
        /// <summary>回车提交。</summary>
        Enter,
        /// <summary>Tab 提交并继续焦点导航。</summary>
        Tab,
        /// <summary>鼠标选择。</summary>
        Mouse,
        /// <summary>输入精确匹配。</summary>
        ExactMatch,
        /// <summary>程序赋值，默认来源。</summary>
        Programmatic,
    }

}
