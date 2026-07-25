using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Services.SystemServices;
using SwiftExport.Application.Services.Systems;
using SwiftExport.Contracts.Apis.SystemApis;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Systems;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure;

namespace SwiftExport.Application.Tests.Systems;

[TestClass]
public sealed class SystemSettingsServiceValidationTests
{
    [TestMethod]
    public void BatchStructure_RejectsEmptyDuplicateAndInvalidVersion()
    {
        StringAssert.Contains(
            SystemBatchSaveSupport.ValidateRequest(new SysViewRouteBatchSaveRequest()),
            "没有需要保存的数据");

        var uid = Guid.NewGuid();
        var duplicate = new SysViewRouteBatchSaveRequest
        {
            UpdateItems =
            [
                new UpdateItemRequest<SysViewRouteDto> { Uid = uid, Version = 1, Data = Route("one") },
                new UpdateItemRequest<SysViewRouteDto> { Uid = uid, Version = 1, Data = Route("two") }
            ]
        };
        StringAssert.Contains(SystemBatchSaveSupport.ValidateRequest(duplicate), "重复修改");

        var invalidVersion = new SysViewRouteBatchSaveRequest
        {
            DeleteItems = [new DeleteItemRequest { Uid = uid, Version = 0 }]
        };
        StringAssert.Contains(SystemBatchSaveSupport.ValidateRequest(invalidVersion), "版本无效");
    }

    [TestMethod]
    public void FailedRepositoryWrite_AbortsAtomicBatch()
    {
        Assert.ThrowsExactly<SystemBatchWriteException>(() =>
            SystemBatchSaveSupport.EnsureWriteSucceeded(
                RepositoryWriteResult.Fail(Guid.NewGuid(), 1, "CONFLICT", "并发冲突"),
                "写入失败"));
    }

    [TestMethod]
    public void SystemItems_AreRecognizedAsProtectedDeletes()
    {
        var protectedId = Guid.NewGuid();
        var normalId = Guid.NewGuid();
        var existing = new Dictionary<Guid, SysDictData>
        {
            [protectedId] = new SysDictData { Uid = protectedId, IsSystem = true },
            [normalId] = new SysDictData { Uid = normalId, IsSystem = false }
        };

        var protectedDeletes = SystemBatchSaveSupport.FindProtectedDeleteIds(
            new[]
            {
                new DeleteItemRequest { Uid = protectedId, Version = 1 },
                new DeleteItemRequest { Uid = normalId, Version = 1 }
            },
            existing,
            item => item.IsSystem);

        CollectionAssert.AreEqual(new[] { protectedId }, protectedDeletes);
    }

    [TestMethod]
    public void DictCodes_OnlyExposeThreeGenericSystemDictionaries()
    {
        CollectionAssert.AreEqual(
            new[] { DictCodes.CustomerType, DictCodes.Incoterm, DictCodes.PaymentMethod },
            DictCodes.DictCodeList);

        CollectionAssert.DoesNotContain(DictCodes.DictCodeList, DictCodes.Currency);
        CollectionAssert.DoesNotContain(DictCodes.DictCodeList, DictCodes.ProductCategory);
        CollectionAssert.DoesNotContain(DictCodes.DictCodeList, DictCodes.ProductColor);
    }

    [TestMethod]
    public void SystemDictionaryType_CannotBeDeletedOrRenamedWhenLegacyFlagIsIncorrect()
    {
        var protectedId = Guid.NewGuid();
        var existing = new Dictionary<Guid, SysDictType>
        {
            [protectedId] = new SysDictType
            {
                Uid = protectedId,
                DictCode = DictCodes.CustomerType,
                DictName = "客户类型",
                IsSystem = false
            }
        };

        StringAssert.Contains(
            SysDictTypeService.ValidateSystemDictionaryProtection(
                new SysDictTypeBatchSaveRequest
                {
                    DeleteItems = [new DeleteItemRequest { Uid = protectedId, Version = 1 }]
                },
                existing),
            "不能删除");

        StringAssert.Contains(
            SysDictTypeService.ValidateSystemDictionaryProtection(
                new SysDictTypeBatchSaveRequest
                {
                    UpdateItems =
                    [
                        new UpdateItemRequest<SysDictTypeDto>
                        {
                            Uid = protectedId,
                            Version = 1,
                            Data = new SysDictTypeDto
                            {
                                DictCode = "Renamed",
                                DictName = "客户类型"
                            }
                        }
                    ]
                },
                existing),
            "编码不能修改");
    }

    [TestMethod]
    public void CustomDictionaryType_AllowsUpdateAndDeleteProtectionCheck()
    {
        var customId = Guid.NewGuid();
        var existing = new Dictionary<Guid, SysDictType>
        {
            [customId] = new SysDictType
            {
                Uid = customId,
                DictCode = "CustomStatus",
                DictName = "自定义状态",
                IsSystem = false
            }
        };

        Assert.IsNull(SysDictTypeService.ValidateSystemDictionaryProtection(
            new SysDictTypeBatchSaveRequest
            {
                UpdateItems =
                [
                    new UpdateItemRequest<SysDictTypeDto>
                    {
                        Uid = customId,
                        Version = 1,
                        Data = new SysDictTypeDto
                        {
                            DictCode = "CustomStatus2",
                            DictName = "自定义状态"
                        }
                    }
                ]
            },
            existing));

        Assert.IsNull(SysDictTypeService.ValidateSystemDictionaryProtection(
            new SysDictTypeBatchSaveRequest
            {
                DeleteItems = [new DeleteItemRequest { Uid = customId, Version = 1 }]
            },
            existing));
    }

    [TestMethod]
    public void UpdateMapping_PreservesServerOwnedFields()
    {
        var uid = Guid.NewGuid();
        var createdDate = new DateTime(2026, 1, 2);
        var entity = new SysDictData
        {
            Uid = uid,
            Version = 3,
            IsDeleted = false,
            CreatedDate = createdDate,
            CreatedBy = "server",
            DictCode = "OLD",
            DataNameEn = "Old",
            DataNameZh = "旧值"
        };
        var dto = new SysDictDataDto
        {
            Uid = Guid.NewGuid(),
            IsDeleted = true,
            CreatedDate = DateTime.Now,
            CreatedBy = "client",
            DictCode = "NEW",
            DataNameEn = "New",
            DataNameZh = "新值"
        };

        SystemBatchSaveSupport.AdaptPreservingServerFields(dto, entity);

        Assert.AreEqual(uid, entity.Uid);
        Assert.IsFalse(entity.IsDeleted);
        Assert.AreEqual(createdDate, entity.CreatedDate);
        Assert.AreEqual("server", entity.CreatedBy);
        Assert.AreEqual("NEW", entity.DictCode);
    }

    [TestMethod]
    public void ViewRoute_FinalKeysMustBeUniqueIgnoringCase()
    {
        StringAssert.Contains(SysViewRouteService.ValidateFinalKeys(new[] { "Products", "products" }), "不能重复");
        Assert.IsNull(SysViewRouteService.ValidateFinalKeys(new[] { "Products", "Suppliers" }));
    }

    [TestMethod]
    public void Currency_RequiresExactlyOneEnabledBaseWithRateOne()
    {
        StringAssert.Contains(
            SysDicCurrencyService.ValidateFinalState(
            [
                new SysDicCurrencyService.CurrencyState("CNY", false, true, 1m),
                new SysDicCurrencyService.CurrencyState("USD", false, true, 7m)
            ]),
            "恰好存在一个本位币");

        StringAssert.Contains(
            SysDicCurrencyService.ValidateFinalState(
            [
                new SysDicCurrencyService.CurrencyState("CNY", true, true, 1m),
                new SysDicCurrencyService.CurrencyState("USD", true, true, 1m)
            ]),
            "恰好存在一个本位币");

        Assert.IsNull(SysDicCurrencyService.ValidateFinalState(
        [
            new SysDicCurrencyService.CurrencyState("CNY", true, true, 1m),
            new SysDicCurrencyService.CurrencyState("USD", false, true, 7m)
        ]));
    }

    [TestMethod]
    public void CurrencyAndExchange_RejectInvalidRatesAndPairs()
    {
        var currency = new SysDicCurrencyDto
        {
            CurrencyCode = "USD",
            CurrencyNameZh = "美元",
            Precision = 2,
            RateToBase = 0,
            IsBase = false,
            IsEnabled = true
        };
        StringAssert.Contains(SysDicCurrencyService.NormalizeAndValidate(currency, 1, "新增"), "必须大于 0");

        var exchange = new SysDicCurrencyExchangeDto
        {
            FromCurrencyCode = "USD",
            ToCurrencyCode = "usd",
            Precision = 6,
            Rate = 1m,
            IsEnabled = true
        };
        StringAssert.Contains(SysDicCurrencyExchangeService.NormalizeAndValidate(exchange, 1, "新增"), "不能相同");
    }

    [TestMethod]
    public void DictTypeGraph_RejectsMissingParentCyclesAndDuplicateCodes()
    {
        var missingId = Guid.NewGuid();
        var orphan = Guid.NewGuid();
        StringAssert.Contains(SysDictTypeService.ValidateFinalGraph(new Dictionary<Guid, SysDictTypeService.DictTypeNode>
        {
            [orphan] = new(orphan, missingId, "ORPHAN", null)
        }), "上级不存在");

        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        StringAssert.Contains(SysDictTypeService.ValidateFinalGraph(new Dictionary<Guid, SysDictTypeService.DictTypeNode>
        {
            [first] = new(first, second, "ONE", null),
            [second] = new(second, first, "TWO", null)
        }), "循环引用");

        StringAssert.Contains(SysDictTypeService.ValidateFinalGraph(new Dictionary<Guid, SysDictTypeService.DictTypeNode>
        {
            [first] = new(first, null, "Code", null),
            [second] = new(second, null, "code", null)
        }), "不能重复");
    }

    [TestMethod]
    public void DictData_RequiresCodeAndBothNames()
    {
        var dto = new SysDictDataDto { DictCode = "Color", DataNameEn = "", DataNameZh = "颜色" };
        StringAssert.Contains(SysDictDataService.NormalizeAndValidate(dto, 3, "新增"), "英文名称不能为空");
    }

    [TestMethod]
    public void SystemRefitApis_ExposeTypedAllPageAndBatchRoutes()
    {
        foreach (var apiType in new[]
        {
            typeof(ISysViewRouteApi),
            typeof(ISysDicCurrencyApi),
            typeof(ISysDicCurrencyExchangeApi),
            typeof(ISysDictTypeApi),
            typeof(ISysDictDataApi)
        })
        {
            AssertRoute(apiType, "GetAllAsync", "GetAttribute", "/all");
            AssertRoute(apiType, "GetPagedAsync", "PostAttribute", "/page");
            AssertRoute(apiType, "BatchSaveAsync", "PostAttribute", "/batch-save");
        }
    }

    [TestMethod]
    public void Infrastructure_RegistersDedicatedDictTypeRepository()
    {
        var services = new ServiceCollection();
        services.AddInfrastructureServices();
        Assert.IsTrue(services.Any(descriptor =>
            descriptor.ServiceType == typeof(ISysDictTypeRepository)
            && descriptor.ImplementationType?.Name == "SysDictTypeRepository"));
    }

    [TestMethod]
    public void TargetSystemServicesAndRepositories_HaveNoNotImplementedPlaceholder()
    {
        var files = new[]
        {
            Path.Combine("SwiftExport.Application", "Services", "SystemServices", "SysViewRouteService.cs"),
            Path.Combine("SwiftExport.Application", "Services", "Systems", "SysDicCurrencyService.cs"),
            Path.Combine("SwiftExport.Application", "Services", "Systems", "SysDicCurrencyExchangeService.cs"),
            Path.Combine("SwiftExport.Application", "Services", "Systems", "SysDictTypeService.cs"),
            Path.Combine("SwiftExport.Application", "Services", "Systems", "SysDictDataService.cs"),
            Path.Combine("SwiftExport.Infrastructure", "Repositories", "Systems", "SysDictDataRepository.cs")
        };

        foreach (var file in files)
            Assert.IsFalse(File.ReadAllText(Path.Combine(Root(), file)).Contains("NotImplementedException", StringComparison.Ordinal), file);
    }

    private static SysViewRouteDto Route(string key) => new()
    {
        TargetViewKey = key,
        DisplayName = key,
        ViewModelTypeName = $"Views.{key}ViewModel"
    };

    private static void AssertRoute(Type apiType, string methodName, string attributeName, string expectedPath)
    {
        var method = apiType.GetMethod(methodName);
        Assert.IsNotNull(method, $"{apiType.Name}.{methodName}");
        var attribute = method.GetCustomAttributes(false).SingleOrDefault(x => x.GetType().Name == attributeName);
        Assert.IsNotNull(attribute, $"{apiType.Name}.{methodName} 缺少 {attributeName}");
        var path = attribute.GetType().GetProperty("Path")?.GetValue(attribute) as string;
        Assert.AreEqual(expectedPath, path, $"{apiType.Name}.{methodName}");
    }

    private static string Root()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) return directory.FullName;
        Assert.Fail("无法定位仓库根目录。");
        return string.Empty;
    }
}
