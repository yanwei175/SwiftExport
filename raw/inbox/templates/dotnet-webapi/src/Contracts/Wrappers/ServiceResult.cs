namespace {{ProjectName}}.Contracts.Wrappers;

public class ServiceResult<T>
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public T? Data { get; set; }

    public static ServiceResult<T> Ok(T data, string message = "成功")
        => new() { Success = true, Data = data, Message = message };

    public static ServiceResult<T> Fail(string message)
        => new() { Success = false, Message = message };
}
