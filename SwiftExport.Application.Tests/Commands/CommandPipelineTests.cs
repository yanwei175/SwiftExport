using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Commands;
using SwiftExport.Application.Commands.Validation;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Logging;

namespace SwiftExport.Application.Tests.Commands;

/// <summary>验证公共入口的调用次序、异步取消传递、结果分类与单次结构化完成事件。</summary>
[TestClass]
public sealed class CommandPipelineTests
{
    /// <summary>权限拒绝先于请求验证，且不会访问回执或业务执行器。</summary>
    [TestMethod]
    public async Task Authorization_PrecedesValidationAndExecution()
    {
        var f = new Fixture();
        var executed = false;
        var result = await f.RunAsync(new Request(0), _ => Task.FromResult<ServiceResult<int>?>(
            ServiceResult<int>.Fail(ServiceErrorCodes.Forbidden, "无权限。")), _ =>
        {
            executed = true;
            throw new AssertFailedException("无权限时不能执行命令。");
        });
        Assert.IsFalse(result.Success);
        Assert.AreEqual(ServiceErrorCodes.Forbidden, result.Error.Code);
        Assert.IsFalse(f.Validated);
        Assert.IsFalse(executed);
        f.AssertCompletion("Rejected", ServiceErrorCodes.Forbidden);
    }

    /// <summary>异步 Validator 接收原取消标识；输入错误阻止执行委托并仅记一次完成事件。</summary>
    [TestMethod]
    public async Task Validation_IsAsyncAndStopsExecution()
    {
        var f = new Fixture();
        using var source = new CancellationTokenSource();
        var exception = await Assert.ThrowsAsync<CommandValidationException>(() => f.RunAsync(new Request(0),
            _ => Task.FromResult<ServiceResult<int>?>(null), _ => throw new AssertFailedException("验证失败不得执行。"), source.Token));
        Assert.AreEqual("Quantity", exception.Field);
        Assert.AreEqual(source.Token, f.SeenToken);
        f.AssertCompletion("Rejected", ServiceErrorCodes.Validation);
    }

    /// <summary>首次成功、回执重放与失败 ServiceResult 各自记录真实 Outcome。</summary>
    [TestMethod]
    public async Task Result_MapsSuccessReplayAndFailure()
    {
        foreach (var (replay, expected) in new[] { (false, "Succeeded"), (true, "Replayed") })
        {
            var f = new Fixture();
            var result = await f.RunAsync(new Request(1), _ => Task.FromResult<ServiceResult<int>?>(null), _ =>
                Task.FromResult(ServiceResult<CommandExecutionResult<int>>.Ok(new(42, replay))));
            Assert.IsTrue(result.Success);
            Assert.AreEqual(42, result.Data);
            f.AssertCompletion(expected, null);
        }
        var rejected = new Fixture();
        var failure = await rejected.RunAsync(new Request(1), _ => Task.FromResult<ServiceResult<int>?>(null), _ =>
            Task.FromResult(ServiceResult<CommandExecutionResult<int>>.Fail(ServiceErrorCodes.VersionConflict, "版本过期。", "ExpectedVersion")));
        Assert.AreEqual(ServiceErrorCodes.VersionConflict, failure.Error.Code);
        Assert.AreEqual("ExpectedVersion", failure.Error.Field);
        rejected.AssertCompletion("Rejected", ServiceErrorCodes.VersionConflict);
    }

    /// <summary>锁繁忙、结果未知、请求取消与未处理异常不能被记录为成功。</summary>
    [TestMethod]
    public async Task BusyUnknownCancellationAndException_HaveDistinctOutcomes()
    {
        foreach (var (code, outcome) in new[] { (ServiceErrorCodes.CommandBusy, "Busy"), (ServiceErrorCodes.OutcomeUnknown, "Unknown") })
        {
            var f = new Fixture();
            await f.RunAsync(new Request(1), _ => Task.FromResult<ServiceResult<int>?>(null), _ =>
                Task.FromResult(ServiceResult<CommandExecutionResult<int>>.Fail(code, "请求未完成。")));
            f.AssertCompletion(outcome, code);
        }
        var failed = new Fixture();
        await Assert.ThrowsAsync<InvalidOperationException>(() => failed.RunAsync(new Request(1),
            _ => Task.FromResult<ServiceResult<int>?>(null), _ => throw new InvalidOperationException("模拟技术故障")));
        failed.AssertCompletion("Failed", ServiceErrorCodes.Unexpected);
        var cancelled = new Fixture();
        using var source = new CancellationTokenSource();
        source.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => cancelled.RunAsync(new Request(1),
            _ => Task.FromResult<ServiceResult<int>?>(null), _ => throw new AssertFailedException("取消后不得执行。"), source.Token));
        cancelled.AssertCompletion("Cancelled", null);
    }

    /// <summary>真实 Serilog 事件应保留完成字段，文件模板才能直接输出这些属性。</summary>
    [TestMethod]
    public async Task CompletionFields_ReachSerilogEvent()
    {
        var sink = new EventSink();
        using var serilog = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();
        using var factory = new SerilogLoggerFactory(serilog);
        var validator = new InlineValidator<Request>();
        var services = new ServiceCollection().AddSingleton<IValidator<Request>>(validator).BuildServiceProvider();
        var pipeline = new CommandPipeline(new CommandRequestValidator(services), new CorrelationStub(), new UserStub(),
            factory.CreateLogger<CommandPipeline>());
        var id = Guid.NewGuid();
        await pipeline.ExecuteAsync(new Request(1), id, "Inventory.Test.Draft.Create",
            _ => Task.FromResult<ServiceResult<int>?>(null),
            _ => Task.FromResult(ServiceResult<CommandExecutionResult<int>>.Ok(new(1, false))), default);
        Assert.HasCount(1, sink.Events);
        var fields = sink.Events[0].Properties;
        foreach (var name in new[] { "CommandType", "CommandId", "CorrelationId", "ElapsedMs", "Outcome", "ActorUid" })
            Assert.IsTrue(fields.ContainsKey(name), $"Serilog 缺少 {name} 属性。");
        Assert.AreEqual("\"Succeeded\"", fields["Outcome"].ToString());
    }

    private sealed record Request(int Quantity);

    /// <summary>以异步规则和日志状态快照观察 Pipeline，不连接数据库。</summary>
    private sealed class Fixture
    {
        private readonly CaptureLogger _logger = new();
        private readonly CommandPipeline _pipeline;
        public bool Validated;
        public CancellationToken SeenToken;

        public Fixture()
        {
            var validator = new InlineValidator<Request>();
            validator.RuleFor(x => x.Quantity).MustAsync(async (value, token) =>
            {
                Validated = true;
                SeenToken = token;
                await Task.Yield();
                return value > 0;
            }).WithMessage("数量必须为正数。");
            var services = new ServiceCollection().AddSingleton<IValidator<Request>>(validator).BuildServiceProvider();
            _pipeline = new CommandPipeline(new CommandRequestValidator(services), new CorrelationStub(), new UserStub(), _logger);
        }

        public Task<ServiceResult<int>> RunAsync(Request request, Func<CancellationToken, Task<ServiceResult<int>?>> authorize,
            Func<CancellationToken, Task<ServiceResult<CommandExecutionResult<int>>>> execute, CancellationToken token = default) =>
            _pipeline.ExecuteAsync(request, Guid.NewGuid(), "Inventory.Test.Draft.Create", authorize, execute, token);

        /// <summary>完成事件应恰好一条，且包含可查询关联字段与非负耗时。</summary>
        public void AssertCompletion(string outcome, string? code)
        {
            Assert.HasCount(1, _logger.Events);
            var fields = _logger.Events[0];
            Assert.AreEqual(outcome, fields["Outcome"]);
            Assert.AreEqual(code, fields["ErrorCode"]);
            Assert.AreEqual("Inventory.Test.Draft.Create", fields["CommandType"]);
            Assert.AreEqual("pipeline-test-call", fields["CorrelationId"]);
            Assert.IsTrue((long)fields["ElapsedMs"]! >= 0);
            Assert.IsInstanceOfType<Guid>(fields["CommandId"]);
        }
    }

    private sealed class CorrelationStub : ICorrelationContext { public string CorrelationId => "pipeline-test-call"; }
    private sealed class UserStub : ICurrentUser
    {
        public Guid? UserId { get; } = Guid.NewGuid();
        public string UserName => "测试";
        public bool IsAuthenticated => true;
    }

    /// <summary>记录 ILogger 属性状态；不依赖格式化文本解析。</summary>
    private sealed class CaptureLogger : ILogger<CommandPipeline>
    {
        public List<Dictionary<string, object?>> Events { get; } = [];
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NoopScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (state is IEnumerable<KeyValuePair<string, object?>> properties)
                Events.Add(properties.ToDictionary(x => x.Key, x => x.Value));
        }
        private sealed class NoopScope : IDisposable
        {
            public static readonly NoopScope Instance = new();
            public void Dispose() { }
        }
    }

    /// <summary>收集真实 Serilog 事件属性，避免只验证 ILogger 文本。</summary>
    private sealed class EventSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}
