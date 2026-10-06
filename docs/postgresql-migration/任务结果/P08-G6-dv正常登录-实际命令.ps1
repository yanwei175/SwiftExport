# 使用当前原宿主/正常dv密码完成只读验收；凭据及JWT只在内存，不修改账号或用户配置。
$ErrorActionPreference='Stop'
Set-Location 'D:\Documents\source\CSharp\SwiftExport'
$taskRoot=(Get-Location).Path
$taskResultPath=Join-Path $taskRoot 'docs/postgresql-migration/任务结果/P08-G6-dv正常登录-HTTP证据.json'
$taskConfig=Get-Content SwiftExport.WebApi/appsettings.json -Raw|ConvertFrom-Json
$taskConnection=[System.Data.Common.DbConnectionStringBuilder]::new()
$taskConnection.set_ConnectionString([string]$taskConfig.ConnectionStrings.Default)
if([string]$taskConnection.get_Item('Host') -ne '192.168.3.12' -or [string]$taskConnection.get_Item('Database') -ne 'dv_next'){throw '目标不符，停止且不输出连接'}
$taskPassword=[Environment]::GetEnvironmentVariable('SWIFTEXPORT_RUNTIME_DV_PASSWORD','User')
if([string]::IsNullOrWhiteSpace($taskPassword)){throw '实际用户dv应用密码不存在'}
$taskOptions=if($taskConnection.ContainsKey('Options')){[string]$taskConnection.get_Item('Options')}else{''}
$taskConnection.set_Item('Options',($taskOptions+' -c default_transaction_read_only=on').Trim())
$taskConnection.set_Item('Application Name','PG-G6-DV-Readonly-20261006')
$taskNames=@('ConnectionStrings__Default','Auth__UseDevUser','ASPNETCORE_ENVIRONMENT')
$taskPrevious=@{}
foreach($taskName in $taskNames){$taskPrevious[$taskName]=[Environment]::GetEnvironmentVariable($taskName,'Process')}
$taskListener=[System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback,0)
$taskListener.Start(); $taskPort=$taskListener.LocalEndpoint.Port; $taskListener.Stop()
$taskProcess=$null; $taskClient=$null; $taskToken=$null
$taskFacts=[ordered]@{recorded_at=(Get-Date).ToString('o');instance='192.168.3.12:5432';database='dv_next';user_name='dv';dev_user_enabled=$false;database_default_read_only=$true;requests=@();port=$taskPort;success=$false;stage='startup';process_stopped=$false}
$taskExit=1
try{
    [Environment]::SetEnvironmentVariable('ConnectionStrings__Default',$taskConnection.get_ConnectionString(),'Process')
    [Environment]::SetEnvironmentVariable('Auth__UseDevUser','false','Process')
    [Environment]::SetEnvironmentVariable('ASPNETCORE_ENVIRONMENT','Development','Process')
    $taskProcess=Start-Process -FilePath 'dotnet' -ArgumentList @('bin/Debug/net10.0/SwiftExport.WebApi.dll','--urls',('http://127.0.0.1:'+$taskPort)) -WorkingDirectory (Join-Path $taskRoot 'SwiftExport.WebApi') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $taskRoot 'TestResults/postgresql-migration/g6-dv-host.stdout.log') -RedirectStandardError (Join-Path $taskRoot 'TestResults/postgresql-migration/g6-dv-host.stderr.log')
    $taskFacts.process_id=$taskProcess.Id
    # 最多等待10秒，仅检测本人新端口；不依赖已有服务器或重复发登录请求。
    $taskReady=$false
    for($taskAttempt=0;$taskAttempt -lt 40;$taskAttempt++){
        if($taskProcess.HasExited){break}
        $taskSocket=[System.Net.Sockets.TcpClient]::new()
        try{$taskSocket.Connect('127.0.0.1',$taskPort);$taskReady=$true}catch{}finally{$taskSocket.Dispose()}
        if($taskReady){break}; Start-Sleep -Milliseconds 250
    }
    if(-not $taskReady){throw '本人宿主未就绪'}
    $taskClient=[System.Net.Http.HttpClient]::new();$taskClient.BaseAddress=[Uri]('http://127.0.0.1:'+$taskPort);$taskClient.Timeout=[TimeSpan]::FromSeconds(15)
    $taskFacts.stage='anonymous-me'
    $taskResponse=$taskClient.GetAsync('/api/auth/me').GetAwaiter().GetResult()
    $taskFacts.requests+=@{method='GET';path='/api/auth/me';status=[int]$taskResponse.StatusCode;identity='anonymous'}
    if([int]$taskResponse.StatusCode -ne 401){throw '匿名边界不符'};$taskResponse.Dispose()
    $taskFacts.stage='normal-login'
    $taskBody=@{UserName='dv';Password=$taskPassword}|ConvertTo-Json -Compress
    $taskContent=[System.Net.Http.StringContent]::new($taskBody,[System.Text.Encoding]::UTF8,'application/json')
    try{$taskResponse=$taskClient.PostAsync('/api/auth/login',$taskContent).GetAwaiter().GetResult()}finally{$taskContent.Dispose();$taskBody=$null;$taskPassword=$null}
    $taskFacts.requests+=@{method='POST';path='/api/auth/login';status=[int]$taskResponse.StatusCode;identity='normal-dv-password'}
    if([int]$taskResponse.StatusCode -ne 200){throw '正常dv登录未成功'}
    $taskToken=$taskResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult()|ConvertFrom-Json;$taskResponse.Dispose()
    if([string]::IsNullOrWhiteSpace($taskToken.Access_token)){throw '原JWT缺失'}
    $taskClient.DefaultRequestHeaders.Authorization=[System.Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer',$taskToken.Access_token)
    $taskFacts.stage='authenticated-me'
    $taskResponse=$taskClient.GetAsync('/api/auth/me').GetAwaiter().GetResult()
    $taskFacts.requests+=@{method='GET';path='/api/auth/me';status=[int]$taskResponse.StatusCode;identity='original-jwt'}
    if([int]$taskResponse.StatusCode -ne 200){throw 'JWT身份读取未成功'}
    $taskMe=$taskResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult()|ConvertFrom-Json;$taskResponse.Dispose()
    if($taskMe.UserName -ne 'dv' -or [string]::IsNullOrWhiteSpace($taskMe.UserId)){throw '实际身份不符'}
    $taskFacts.user_id=$taskMe.UserId;$taskFacts.permission_count=@($taskMe.Permissions).Count;$taskFacts.role_codes=@($taskMe.RoleCodes)
    $taskFacts.stage='product-page'
    $taskContent=[System.Net.Http.StringContent]::new('{"PageIndex":1,"PageSize":2,"Keyword":""}',[System.Text.Encoding]::UTF8,'application/json')
    try{$taskResponse=$taskClient.PostAsync('/api/product/page',$taskContent).GetAwaiter().GetResult()}finally{$taskContent.Dispose()}
    $taskFacts.requests+=@{method='POST';path='/api/product/page';status=[int]$taskResponse.StatusCode;identity='original-jwt'}
    if([int]$taskResponse.StatusCode -ne 200){throw '产品只读入口未成功'}
    $taskProducts=$taskResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult()|ConvertFrom-Json;$taskResponse.Dispose()
    if(-not $taskProducts.Success -or $taskProducts.Data.PageIndex -ne 1 -or @($taskProducts.Data.Items).Count -ne 2){throw '产品分页契约不符'}
    $taskFacts.product_total=$taskProducts.Data.TotalCount;$taskFacts.product_items=@($taskProducts.Data.Items).Count
    $taskFacts.success=$true;$taskFacts.stage='completed';$taskExit=0
}catch{
    # 不输出异常正文或请求响应体，避免凭据/令牌进入工具输出；阶段与HTTP码足以定位本次结果。
    $taskFacts.failure='当前阶段未通过，详情按阶段/HTTP状态核对，不自动重置或猜测密码'
}finally{
    if($taskClient){$taskClient.Dispose()};$taskToken=$null;$taskPassword=$null;$taskConnection=$null;$taskConfig=$null
    if($taskProcess -and -not $taskProcess.HasExited){Stop-Process -Id $taskProcess.Id; $taskProcess.WaitForExit(5000)|Out-Null}
    $taskFacts.process_stopped=($null -eq $taskProcess -or $taskProcess.HasExited)
    foreach($taskName in $taskNames){[Environment]::SetEnvironmentVariable($taskName,$taskPrevious[$taskName],'Process')}
    $taskFacts.finished_at=(Get-Date).ToString('o')
    $taskFacts|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $taskResultPath -Encoding utf8
    $taskFacts|ConvertTo-Json -Depth 5|Write-Output
}
exit $taskExit
