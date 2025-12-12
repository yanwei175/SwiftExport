using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Factory;
using SwiftExport.Infrastructure.Global;
using SwiftExport.Infrastructure.Interfaces;
using SwiftExport.Infrastructure.Repositories;
using System;
using System.Data;

namespace SwiftExport.Infrastructure.DI
{
    public static class DI_Infrastructure
    {
        /// <summary>
        /// 主入口：宿主必须传入 IConfiguration（IConfiguration 在宿主中构建）。
        /// connectionName 表示 GetConnectionString(name) 的 name，默认 "Default"。
        /// </summary>
        public static IServiceCollection AddInfrastructureServices(this IServiceCollection services,  string connString)
        {
            //注入数据库连接字符串
            if (string.IsNullOrWhiteSpace(connString)) throw new ArgumentNullException(nameof(connString));

            services.AddSingleton<IDbConnectionFactory>(sp=> new SqlConnectionFactory(connString));

            //注入数据库连接（Scoped，每次请求一个连接实例）
            services.AddScoped<IDbConnection>(sp => sp.GetRequiredService<IDbConnectionFactory>().CreateConnection());

            // 注册 MappingCache（Singleton，全局缓存）
            services.AddSingleton<IMappingCache, MappingCache>();
            services.AddSingleton(typeof(IDataTableFactory<>), typeof(DataTableFactory<>));

            // 注册注册仓储,泛型仓库
            services.AddScoped(typeof(IRepository<>), typeof(DapperRepository<>));
            services.AddScoped<IProductRepository, ProductRepository>();
            services.AddScoped<IBomRepository, BomRepository>();
            services.AddScoped<IViewUserRoleRepository, ViewUserRoleRepository>();
            services.AddScoped<ICustomerProductRepository, CustomerProductRepository>();

            //services.AddScoped<ISalesOrderRepository, SalesOrderRepository>();

            return services;
        }
    }
}