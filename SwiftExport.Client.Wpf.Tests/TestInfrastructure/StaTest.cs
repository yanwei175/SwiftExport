using System.Runtime.ExceptionServices;
using System.Windows.Threading;

namespace SwiftExport.Client.Wpf.Tests.TestInfrastructure;

/// <summary>在独立 STA 线程运行原生 WPF 测试并传播异常。</summary>
internal static class StaTest
{
    /// <summary>运行同步 WPF 测试，复用同一 STA 调度入口。</summary>
    public static void Run(Action action) => RunAsync(() => { action(); return Task.CompletedTask; });

    /// <summary>运行需要 Dispatcher 消息泵的异步测试。</summary>
    public static void RunAsync(Func<Task> action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
                var task = action();
                while (!task.IsCompleted)
                {
                    var frame = new DispatcherFrame();
                    dispatcher.BeginInvoke(
                        DispatcherPriority.Background,
                        new Action(() => frame.Continue = false));
                    Dispatcher.PushFrame(frame);
                }

                task.GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
        {
            ExceptionDispatchInfo.Capture(exception).Throw();
        }
    }
}
