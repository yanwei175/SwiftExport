using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SwiftExport.AppLayer.DTOs;
using SwiftExport.AppLayer.Interfaces;
using SwiftExport.AppLayer.Services;
using SwiftExport.Infrastructure.DI;
using System;

namespace SwiftExport.AppLayer.DI
{
    public static class DI_Application
    {
        /// <summary>
        /// 主入口：宿主必须传入 IConfiguration（IConfiguration 在宿主中构建）。
        /// connectionName 表示 GetConnectionString(name) 的 name，默认 "Default"。
        /// </summary>
        public static IServiceCollection AddApplicationServers(this IServiceCollection services,  IConfiguration configuration)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));

            var connStr = configuration.GetConnectionString("DataBase");

            if(string.IsNullOrWhiteSpace(connStr)) throw new ArgumentNullException(nameof(connStr));

            services.AddAutoMapper(cfg => { }, typeof(ProductProfiles).Assembly);

            //注册基础设施服务
            services.AddInfrastructureServices(connStr);
  
            // 注册应用服务
            services.AddScoped<IProductService, ProductService> ();
            services.AddScoped<IBOMService, BomService>();
            services.AddScoped<IViewUserRoleService, ViewUserRoleService>();
            // 扫描 Application 层程序集里的所有 Profile
            

            return services;
        }
    }
}