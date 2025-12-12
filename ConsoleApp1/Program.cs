using System;
using System.Data;
using System.Data.Common;
using System.IO.Pipes;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SwiftExport.AppLayer.DI;
using SwiftExport.Infrastructure.DI;
using SwiftExport.Infrastructure.Interfaces;

try
{

    var conStr= "Server=localhost;Database=test;User Id=sa;Password=123456; TrustServerCertificate=True";

    var services = new ServiceCollection();
    
    services.AddInfrastructureServices(conStr);

    using var provider = services.BuildServiceProvider();

    // ---- DB 连接测试（使用 IConnServer） ----
    try
    {
        using var scope = provider.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        using var conn = factory.CreateConnection();
        conn.Open();


        Console.WriteLine($"数据库连通性测试结果：{conn.State}");


    }
    catch (Exception ex)
    {
        Console.WriteLine("数据库连通性测试失败： " + ex.Message);
        Console.WriteLine(ex);
    }
}
catch (Exception ex)
{
    Console.WriteLine("启动失败： " + ex.Message);
    Console.WriteLine(ex);
}
