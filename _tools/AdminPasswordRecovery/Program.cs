using SwiftExport.Application.Services.Auth;
using SwiftExport.Tools.AdminPasswordRecovery;

var connectionString = Environment.GetEnvironmentVariable("SWIFTEXPORT_ADMIN_RECOVERY_CONNECTION");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("缺少环境变量 SWIFTEXPORT_ADMIN_RECOVERY_CONNECTION。");
    return 2;
}

Console.Write("现有 Admin 用户名: ");
var userName = Console.ReadLine();
Console.Write("新密码: ");
var password = ReadSecret();
Console.Write("再次输入新密码: ");
var confirmation = ReadSecret();

var executor = new AdminPasswordRecoveryExecutor(
    new SqlAdminPasswordRecoveryStore(connectionString),
    new BcryptPasswordHasher());
var result = await executor.ExecuteAsync(userName, password, confirmation);
password = null;
confirmation = null;
Console.WriteLine(result.Message);
return result.Success ? 0 : 1;

/// <summary>从控制台隐藏读取敏感输入，并支持退格及取消。</summary>
static string ReadSecret()
{
    var buffer = new List<char>();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter)
        {
            Console.WriteLine();
            return new string(buffer.ToArray());
        }
        if (key.Key == ConsoleKey.Backspace)
        {
            if (buffer.Count > 0) buffer.RemoveAt(buffer.Count - 1);
            continue;
        }
        if ((key.Modifiers & ConsoleModifiers.Control) != 0 && key.Key == ConsoleKey.C)
            throw new OperationCanceledException("用户取消了密码恢复。");
        if (!char.IsControl(key.KeyChar)) buffer.Add(key.KeyChar);
    }
}
