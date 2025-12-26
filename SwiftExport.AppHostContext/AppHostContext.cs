using Microsoft.Extensions.DependencyInjection;
using System;

namespace SwiftExport.AppHost
{
    public static class AppHostContext
    {
        public static IServiceProvider Provider { get; private set; }

        public static void Init(IServiceProvider provider)
        {
            Provider = provider;
        }

        //public static T Get<T>() => Provider.GetRequiredService<T>();
    }
}
