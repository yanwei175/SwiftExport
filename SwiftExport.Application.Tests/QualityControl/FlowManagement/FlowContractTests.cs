using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Application.Interfaces.QualityControl.FlowManagement;
using SwiftExport.Application.Services.QualityControl.FlowManagement;
using SwiftExport.Contracts.Apis.QualityControl.FlowManagement;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.QualityControl.FlowManagement;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Common;
using SwiftExport.Core.DatabaseSchema;
using SwiftExport.Core.Entities.QualityControl.FlowManagement;
using SwiftExport.Core.Interfaces.QualityControl.FlowManagement;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.QualityControl.FlowManagement;
using SwiftExport.WebApi.Controllers.QualityControl.FlowManagement;

namespace SwiftExport.Application.Tests.QualityControl.FlowManagement;

/// <summary>验证流量契约的单位、写入边界、HTTP 对齐、SQL 参数化、数据库结构和模块接线。</summary>
[TestClass]
public sealed class FlowContractTests
{
    /// <summary>Controller、Refit 和服务接口的五个入口一一对应，保留取消参数和相同返回类型。</summary>
    [TestMethod]
    public void ApiRoutesAndContractsMatch()
    {
        var controller = typeof(FlowController);
        Assert.IsNotNull(controller.GetCustomAttribute<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>());
        var prefix = controller.GetCustomAttribute<RouteAttribute>()!.Template;
        Assert.AreEqual("api/quality-control/flows", prefix);
        var methods = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.AreEqual(5, methods.Length);
        foreach (var method in methods)
        {
            var api = typeof(IFlowApi).GetMethod(method.Name)!;
            var service = typeof(IFlowService).GetMethod(method.Name)!;
            Assert.AreEqual(method.ReturnType, api.ReturnType, method.Name);
            Assert.AreEqual(method.ReturnType, service.ReturnType, method.Name);
            Assert.AreEqual(typeof(CancellationToken), api.GetParameters().Last().ParameterType);
            Assert.AreEqual(typeof(CancellationToken), method.GetParameters().Last().ParameterType);
            var route = method.GetCustomAttribute<Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute>()!;
            var expected = "/" + prefix + (string.IsNullOrEmpty(route.Template) ? "" : "/" + route.Template.Replace(":guid", ""));
            var refit = api.GetCustomAttributes().OfType<Refit.HttpMethodAttribute>().Single();
            Assert.AreEqual(expected, refit.Path, method.Name);
            Assert.AreEqual(route.HttpMethods.Single(), refit.Method.Method, method.Name);
            foreach (var parameter in method.GetParameters().Where(x => x.GetCustomAttribute<FromBodyAttribute>() is not null))
                Assert.IsNotNull(api.GetParameters().Single(x => x.Name == parameter.Name).GetCustomAttribute<BodyAttribute>());
        }
    }

    /// <summary>写入模型不接收客户端快照、审计和删除标记，十三列均为可空十进制，读写口径一致。</summary>
    [TestMethod]
    public void WriteInputsExcludeServerOwnedFacts()
    {
        foreach (var type in new[] { typeof(FlowEditDto), typeof(FlowLineEditDto) })
            foreach (var forbidden in new[] { "SkuSnapshot", "FunctionNameSnapshot", "LimiterSpecificationNameSnapshot", "CreatedBy", "CreatedDate", "IsDeleted", "HeaderId" })
                Assert.IsNull(type.GetProperty(forbidden));
        var fields = typeof(FlowLineEditDto).GetProperties().Where(x => x.PropertyType == typeof(decimal?)).ToArray();
        Assert.AreEqual(13, fields.Length);
        Assert.AreEqual(8, fields.Count(x => x.Name.EndsWith("BarLMin")));
        Assert.AreEqual(5, fields.Count(x => x.Name.EndsWith("PsiGpm")));
        foreach (var property in fields)
        {
            Assert.AreEqual(typeof(decimal?), typeof(FlowLine).GetProperty(property.Name)!.PropertyType);
            Assert.AreEqual(typeof(decimal?), typeof(FlowLineDto).GetProperty(property.Name)!.PropertyType);
            Assert.IsNull(property.GetValue(new FlowLineEditDto()));
        }
    }

    /// <summary>实体生成器提供功能唯一键、非负及至少一个测量点约束，不建立数据库外键。</summary>
    [TestMethod]
    public void SchemaProtectsIdentityAndMeasurements()
    {
        var sql = new PostgreSqlSchemaGenerator().Generate(typeof(BaseEntity).Assembly);
        StringAssert.Contains(sql, "CREATE TABLE \"public\".\"flow_header\"");
        StringAssert.Contains(sql, "CREATE TABLE \"public\".\"flow_line\"");
        StringAssert.Contains(sql, "CREATE UNIQUE INDEX \"ux_flow_line_header_function\"");
        StringAssert.Contains(sql, "\"test_date\" date NOT NULL");
        StringAssert.Contains(sql, "fn_flow_header_immutable_identity");
        Assert.IsFalse(Regex.IsMatch(sql, @"\bFOREIGN\s+KEY\b|\bREFERENCES\b", RegexOptions.IgnoreCase));
        var checks = typeof(FlowLine).GetCustomAttributes<DbCheckAttribute>().ToDictionary(x => x.Name);
        foreach (var column in EntityMetadataCache.GetMetadata<FlowLine>().Columns.Where(x => x.Property.PropertyType == typeof(decimal?)))
        {
            StringAssert.Contains(sql, $"\"{column.ColumnName}\" numeric(18,4)");
            StringAssert.Contains(checks["CK_FlowLine_Measurements"].Expression, $"\"{column.ColumnName}\" >= 0");
            StringAssert.Contains(checks["CK_FlowLine_HasMeasurement"].Expression, $"\"{column.ColumnName}\"");
        }
    }

    /// <summary>SKU 特殊字符只进入参数值，日期含上下界，查询不引入用户 SQL。</summary>
    [TestMethod]
    public void SearchIsParameterizedAndDateInclusive()
    {
        const string input = "%' OR TRUE; --";
        var material = Guid.NewGuid();
        var (where, args) = PostgreSqlFlowRepository.BuildSearch(new()
        {
            MaterialId = material, SkuSnapshot = input,
            TestDateFrom = new(2026, 10, 1, 15, 0, 0), TestDateTo = new(2026, 10, 8, 21, 0, 0)
        });
        Assert.IsFalse(where.Contains(input, StringComparison.Ordinal));
        StringAssert.Contains(where, "h.material_id=@MaterialId");
        StringAssert.Contains(where, "strpos(lower(h.sku_snapshot),lower(@Sku))>0");
        StringAssert.Contains(where, "h.test_date>=@DateFrom");
        StringAssert.Contains(where, "h.test_date<=@DateTo");
        Assert.AreEqual(input, args.Get<string>("Sku"));
        Assert.AreEqual(material, args.Get<Guid>("MaterialId"));
        Assert.AreEqual(new DateTime(2026, 10, 8), args.Get<DateTime>("DateTo"));
    }

    /// <summary>四项操作权限均属于品质管理，DI 接线和系统字典分类真实登记。</summary>
    [TestMethod]
    public void PermissionsDictionariesAndDiAreRegistered()
    {
        foreach (var key in new[] { PermissionKeys.QualityControlFlowView, PermissionKeys.QualityControlFlowCreate,
            PermissionKeys.QualityControlFlowEdit, PermissionKeys.QualityControlFlowDelete })
            Assert.AreEqual("QualityControl", PermissionCatalog.Find(key)!.GroupKey);
        Assert.Contains(DictCodes.FlowTestFunction, DictCodes.DictCodeList);
        Assert.Contains(DictCodes.FlowLimiterSpecification, DictCodes.DictCodeList);
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        SwiftExport.Application.DependencyInjection.AddApplicationServices(services);
        SwiftExport.Infrastructure.DependencyInjection.AddInfrastructureServices(services);
        Assert.IsTrue(services.Any(x => x.ServiceType == typeof(IFlowService) && x.ImplementationType == typeof(FlowService)));
        Assert.IsTrue(services.Any(x => x.ServiceType == typeof(IFlowRepository) && x.ImplementationType == typeof(PostgreSqlFlowRepository)));
    }

    /// <summary>增量脚本必须包含当前实体生成的两表定义，初始化只包含明确要求的默认字典项。</summary>
    [TestMethod]
    public void DeliveryScriptsMatchSchemaAndSeedScope()
    {
        var root = RepositoryRoot();
        var directory = Path.Combine(root, "SwiftExport.Infrastructure", "Database", "QualityControl", "FlowManagement");
        var migration = File.ReadAllText(Path.Combine(directory, "FlowSchema.sql")).Replace("\r\n", "\n");
        var baseline = new PostgreSqlSchemaGenerator().Generate(typeof(BaseEntity).Assembly);
        foreach (Match match in Regex.Matches(baseline, "(?ms)^CREATE TABLE \"public\"\\.\"flow_(?:header|line)\" \\(.*?^\\);"))
            StringAssert.Contains(migration, match.Value);
        Assert.AreEqual(2, Regex.Matches(migration, "^CREATE TABLE ", RegexOptions.Multiline).Count);
        Assert.IsFalse(Regex.IsMatch(migration, @"\bFOREIGN\s+KEY\b|\bREFERENCES\b", RegexOptions.IgnoreCase));
        StringAssert.Contains(migration, "流量模块表已存在");
        var seed = File.ReadAllText(Path.Combine(directory, "FlowDictionarySeed.sql"));
        StringAssert.Contains(seed, $"('{DictCodes.FlowTestFunction}','Default','Default'");
        StringAssert.Contains(seed, $"('{DictCodes.FlowLimiterSpecification}','Unlimited','不限流'");
        StringAssert.Contains(seed, "IN SHARE ROW EXCLUSIVE MODE");
        Assert.AreEqual(2, Regex.Matches(seed, "NOT EXISTS", RegexOptions.IgnoreCase).Count);
        Assert.IsFalse(Regex.IsMatch(seed, @"\bUPDATE\b|\bDELETE\b|\bTRUNCATE\b", RegexOptions.IgnoreCase));
    }

    /// <summary>定位当前仓库以读取本地交付脚本，不依赖测试运行目录。</summary>
    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "SwiftExport.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException("无法定位流量管理交付脚本。");
    }
}
