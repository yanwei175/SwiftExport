using System;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace SwiftExport.Client.VSTO.Hosting.Threading
{
    /// <summary>使用明确捕获的 WPF Dispatcher，不在后台寻找 CurrentDispatcher。</summary>
    public sealed class WpfUiDispatcher : IUiDispatcher
    {
        private readonly Dispatcher dispatcher;
        /// <summary>绑定 UI 创建线程。</summary>
        public WpfUiDispatcher(Dispatcher dispatcher) { this.dispatcher = dispatcher; }
        /// <inheritdoc />
        public Task InvokeAsync(Action action) { return dispatcher.InvokeAsync(action).Task; }
    }
}
