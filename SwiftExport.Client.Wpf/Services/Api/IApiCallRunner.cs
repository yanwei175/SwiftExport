using SwiftExport.Contracts.Wrappers;

namespace SwiftExportClient.Services.Api;

public interface IApiCallRunner
{
    Task<ServiceResult<T>> RunAsync<T>(
        Func<Task<ServiceResult<T>>> action,
        string failureMessage = "请求失败");

    Task<ServiceResult<T>> RunValueAsync<T>(
        Func<Task<T>> action,
        string successMessage = "成功",
        string failureMessage = "请求失败");
}
