using System;
using System.Threading.Tasks;

namespace SwiftExport.Client.VSTO.Hosting.Threading
{
    /// <summary>将状态接纳投递到创建 WPF 的线程。</summary>
    public interface IUiDispatcher
    {
        /// <summary>异步执行短小状态操作；宿主已释放时不再执行。</summary>
        Task InvokeAsync(Action action);
    }
    /// <summary>将短小 Excel COM 操作投递到捕获的 Excel STA。</summary>
    public interface IExcelDispatcher : IUiDispatcher { }
}
