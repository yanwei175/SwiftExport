using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SwiftExport.Client.VSTO.Hosting.Threading
{
    /// <summary>利用 Excel STA 创建的控件句柄调度，不依赖回调的 SynchronizationContext。</summary>
    public sealed class ExcelDispatcher : IExcelDispatcher, IDisposable
    {
        private readonly Control control;
        private readonly object gate = new object();
        private readonly HashSet<TaskCompletionSource<bool>> pending = new HashSet<TaskCompletionSource<bool>>();
        private bool disposed;
        /// <summary>必须在 Excel 主 STA 构造并创建句柄。</summary>
        public ExcelDispatcher()
        {
            if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA) throw new InvalidOperationException("必须在 Excel STA 初始化。");
            control = new Control();
            var handle = control.Handle;
        }
        /// <inheritdoc />
        public Task InvokeAsync(Action action)
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (gate)
            {
                if (disposed) { completion.SetCanceled(); return completion.Task; }
                pending.Add(completion);
                try
                {
                    control.BeginInvoke(new Action(() =>
                    {
                        lock (gate)
                        {
                            pending.Remove(completion);
                            if (disposed) { completion.TrySetCanceled(); return; }
                            try { action(); completion.TrySetResult(true); }
                            catch (Exception ex) { completion.TrySetException(ex); }
                        }
                    }));
                }
                catch (InvalidOperationException) { pending.Remove(completion); completion.TrySetCanceled(); }
            }
            return completion.Task;
        }
        /// <summary>在创建线程释放句柄，并结束所有排队等待。</summary>
        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
                foreach (var item in pending) item.TrySetCanceled();
                pending.Clear();
                control.Dispose();
            }
        }
    }
}
