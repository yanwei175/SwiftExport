using SwiftExport.Contracts.Apis.SystemApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.SystemDtos.SystemParameters;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;

namespace SwiftExport.Client.Wpf.Tests.TestInfrastructure;

/// <summary>隔离网络和登录持久化的系统参数客户端夹具，使用真实缓存与身份服务。</summary>
internal sealed class SystemParameterClientFixture : IDisposable
{
    /// <summary>建立活动管理员会话，可选择预置已配置地址。</summary>
    public SystemParameterClientFixture(string? value = "https://example.test/files/ProductImages/")
    {
        User.SetCurrentUser(new MeDto { UserId = "admin", UserName = "admin", IsAdmin = true });
        Cache = new SystemParameterCacheService(Api, Runner, Session, User);
        if (value is not null) Cache.ApplySaved(Parameter(value));
    }
    public ParameterApi Api { get; } = new();
    public CurrentUserService User { get; } = new();
    public SessionStub Session { get; } = new();
    public DirectRunner Runner { get; } = new();
    public SystemParameterCacheService Cache { get; }
    /// <summary>创建一个完整参数快照，调用方可指定并发版本。</summary>
    public static SystemParameterDto Parameter(string value, int version = 1) => new()
    { Key = SystemParameterKeys.ProductImagesBaseUrl, Value = value, Version = version, IsConfigured = true, DisplayName = "产品图片基础地址", Description = "测试参数" };
    /// <inheritdoc />
    public void Dispose() => Cache.Dispose();

    /// <summary>允许测试控制读取和保存回执完成顺序。</summary>
    internal sealed class ParameterApi : ISystemParameterApi
    {
        public int Reads { get; private set; }
        public int Writes { get; private set; }
        public UpdateSystemParameterRequest? LastRequest { get; private set; }
        public string? LastKey { get; private set; }
        public Func<Task<ServiceResult<IReadOnlyList<SystemParameterDto>>>> Read { get; set; } = () => Task.FromResult(Result("https://example.test/files/ProductImages/"));
        public Func<UpdateSystemParameterRequest, Task<ServiceResult<SystemParameterDto>>> Save { get; set; } = request => Task.FromResult(ServiceResult<SystemParameterDto>.Ok(Parameter(request.Value, request.ExpectedVersion + 1)));
        /// <summary>构造读取回执。</summary>
        public static ServiceResult<IReadOnlyList<SystemParameterDto>> Result(string value, int version = 1) => ServiceResult<IReadOnlyList<SystemParameterDto>>.Ok([Parameter(value, version)]);
        /// <inheritdoc />
        public Task<ServiceResult<IReadOnlyList<SystemParameterDto>>> ListAsync(CancellationToken cancellationToken = default) { Reads++; return Read(); }
        /// <inheritdoc />
        public Task<ServiceResult<SystemParameterDto>> UpdateAsync(string key, UpdateSystemParameterRequest request, CancellationToken cancellationToken = default)
        { Writes++; LastKey = key; LastRequest = request; return Save(request); }
    }
    /// <summary>直接执行 API 委托，异常由测试直接观察。</summary>
    internal sealed class DirectRunner : IApiCallRunner
    {
        /// <inheritdoc />
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, string failureMessage = "请求失败") => action();
        /// <inheritdoc />
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, CancellationToken cancellationToken, string failureMessage = "请求失败") => action();
        /// <inheritdoc />
        public async Task<ServiceResult<T>> RunValueAsync<T>(Func<Task<T>> action, string successMessage = "成功", string failureMessage = "请求失败") => ServiceResult<T>.Ok(await action());
    }
    /// <summary>只触发会话结束通知，不写入凭据。</summary>
    internal sealed class SessionStub : ISessionService
    {
        /// <inheritdoc />
        public event EventHandler<SessionEndedEventArgs>? SessionEnded;
        /// <inheritdoc />
        public void ClearSession() => SessionEnded?.Invoke(this, new SessionEndedEventArgs(SessionEndReason.Logout));
        /// <inheritdoc />
        public void EndSession(SessionEndReason reason) => SessionEnded?.Invoke(this, new SessionEndedEventArgs(reason));
    }
}
