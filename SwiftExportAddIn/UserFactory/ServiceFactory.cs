using Microsoft.Extensions.DependencyInjection;
using SwiftExportAddIn.UserFactory;
using System;

namespace SwiftExportVSTO.静态类
{
    public class ServiceFactory : IServerFactory
    {
        private readonly IServiceProvider _provider;
        //private static readonly object _lock = new object();

        /// <summary>
        /// 初始化容器，只调用一次
        /// </summary>
        public ServiceFactory(IServiceProvider provider)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        /// <summary>
        /// 获取服务（全局单例）
        /// </summary>
        public T GetSingletonService<T>()
        {
            var service = _provider.GetService<T>();
            return service == null ? throw new InvalidOperationException($"未找到服务: {typeof(T).FullName}") : service;
        }

        /// <summary>
        /// 创建 Scope（支持 Scoped 生命周期）
        /// </summary>
        public IServiceScope CreateScope()
        {
            return _provider.CreateScope();
        }
    }

}
