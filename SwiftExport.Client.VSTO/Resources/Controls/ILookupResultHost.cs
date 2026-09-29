using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections;

namespace SwiftExport.Client.VSTO.Resources.Controls
{

    /// <summary>
    /// LookupTextBox 自定义候选视图的内部交互契约。
    /// </summary>
    internal interface ILookupResultHost
    {
        /// <summary>是否存在候选。</summary>
        bool HasItems { get; }

        /// <summary>当前键盘候选。</summary>
        object CurrentItem { get; }

        /// <summary>根据来源和文本刷新本地视图。</summary>
        void Refresh(IEnumerable itemsSource, string query);

        /// <summary>选择首项。</summary>
        void SelectFirst();

        /// <summary>按偏移移动选择。</summary>
        void MoveSelection(int offset);

        /// <summary>尝试解析精确匹配项。</summary>
        bool TryResolveExact(string text, out object item);
    }

}
