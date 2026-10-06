# 用户明确批准仅运行库新1配置2字段；原JWT/原服务保存，旧行保护，不修改账号或结构。
$ErrorActionPreference='Stop'
Set-Location 'D:\Documents\source\CSharp\SwiftExport'
$taskRoot=(Get-Location).Path
$taskResultPath=Join-Path $taskRoot 'docs/postgresql-migration/任务结果/P08-G6-运行库限定写入-HTTP证据.json'
$taskConfig=Get-Content SwiftExport.WebApi/appsettings.json -Raw|ConvertFrom-Json
$taskConnection=[System.Data.Common.DbConnectionStringBuilder]::new()
$taskConnection.set_ConnectionString([string]$taskConfig.ConnectionStrings.Default)
if([string]$taskConnection.get_Item('Host') -ne '192.168.3.12' -or [string]$taskConnection.get_Item('Database') -ne 'dv_next'){throw '目标不符，停止且不输出连接'}
$taskPassword=[Environment]::GetEnvironmentVariable('SWIFTEXPORT_RUNTIME_DV_PASSWORD','User')
if([string]::IsNullOrWhiteSpace($taskPassword)){throw '实际用户dv应用密码不存在'}
$taskOptions=if($taskConnection.ContainsKey('Options')){[string]$taskConnection.get_Item('Options')}else{''}
if($taskOptions -match 'default_transaction_read_only=on'){throw '原配置只读，停止且不覆盖用户选项'}
$taskConnection.set_Item('Application Name','PG-G6-DV-Write-20261006')
$taskNames=@('ConnectionStrings__Default','Auth__UseDevUser','ASPNETCORE_ENVIRONMENT')
$taskPrevious=@{}
foreach($taskName in $taskNames){$taskPrevious[$taskName]=[Environment]::GetEnvironmentVariable($taskName,'Process')}
$taskListener=[System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback,0)
$taskListener.Start(); $taskPort=$taskListener.LocalEndpoint.Port; $taskListener.Stop()
$taskProcess=$null; $taskClient=$null; $taskToken=$null
$taskFacts=[ordered]@{recorded_at=(Get-Date).ToString('o');instance='192.168.3.12:5432';database='dv_next';user_name='dv';dev_user_enabled=$false;database_default_read_only=$false;requests=@();port=$taskPort;success=$false;stage='startup';process_stopped=$false}
$taskExit=1
try{
    [Environment]::SetEnvironmentVariable('ConnectionStrings__Default',$taskConnection.get_ConnectionString(),'Process')
    [Environment]::SetEnvironmentVariable('Auth__UseDevUser','false','Process')
    [Environment]::SetEnvironmentVariable('ASPNETCORE_ENVIRONMENT','Development','Process')
    $taskProcess=Start-Process -FilePath 'dotnet' -ArgumentList @('bin/Debug/net10.0/SwiftExport.WebApi.dll','--urls',('http://127.0.0.1:'+$taskPort)) -WorkingDirectory (Join-Path $taskRoot 'SwiftExport.WebApi') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $taskRoot 'TestResults/postgresql-migration/g6-dv-write-host.stdout.log') -RedirectStandardError (Join-Path $taskRoot 'TestResults/postgresql-migration/g6-dv-write-host.stderr.log')
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
    # 仅本批独立分组的原Create/Update/Get；不删除成功事实，不调用其它业务写端点。
    $taskGroup='PGG6HTTP20261006'
    $taskFacts.group_code=$taskGroup
    function Invoke-TaskProfileRequest([string]$taskMethod,[string]$taskPath,$taskPayload,[int]$taskExpectedStatus) {
        # 返回原JSON并仅记录HTTP码；不输出JWT/请求头，失败保留实际阶段。
        $taskRequest=[System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::new($taskMethod),$taskPath)
        if($null -ne $taskPayload){$taskRequest.Content=[System.Net.Http.StringContent]::new(($taskPayload|ConvertTo-Json -Depth 8 -Compress),[System.Text.Encoding]::UTF8,'application/json')}
        try {
            $taskReply=$taskClient.SendAsync($taskRequest).GetAwaiter().GetResult()
            $taskFacts.requests+=@{method=$taskMethod;path=$taskPath;status=[int]$taskReply.StatusCode;identity='original-jwt'}
            try {
                $taskParsedReply=$taskReply.Content.ReadAsStringAsync().GetAwaiter().GetResult()|ConvertFrom-Json
                if([int]$taskReply.StatusCode -ne $taskExpectedStatus){throw 'HTTP状态未通过'}
                return $taskParsedReply
            }finally{$taskReply.Dispose()}
        }finally{$taskRequest.Dispose()}
    }
    $taskFacts.stage='profile-create'
    $taskCreated=Invoke-TaskProfileRequest 'POST' '/api/excel-read-profiles' @{GroupCode=$taskGroup;ConfigName='PG迁移验收20261006';Remark='仅本批独立配置，无工作簿读取或现有业务影响'} 200
    if(-not $taskCreated.Success -or $taskCreated.Data.Version -ne 1 -or $taskCreated.Data.CanRead -or @($taskCreated.Data.Fields).Count -ne 0){throw '原空配置契约未通过'}
    $taskProfileId=[Guid]$taskCreated.Data.Uid
    if($taskProfileId -eq [Guid]::Empty){throw '新配置UID缺失'}
    $taskFacts.profile_uid=$taskProfileId.ToString()
    $taskFacts.create_version=1
    $taskFacts|ConvertTo-Json -Depth 12|Set-Content -LiteralPath $taskResultPath -Encoding utf8
    $taskFields=@(@{TargetFieldKey='Sku';WorksheetName='Migration';CellAddress='A8';IsDownward=$true},@{TargetFieldKey='Quantity';WorksheetName=$null;CellAddress='D8';IsDownward=$true})
    $taskUpdate=@{ExpectedVersion=1;ConfigName='PG迁移验收20261006-已保存';Remark='G6 dv原JWT保存';EndMode=1;LastRowColumn='A';SummaryCellAddress=$null;Fields=$taskFields}
    $taskFacts.stage='profile-update'
    $taskSaved=Invoke-TaskProfileRequest 'PUT' ('/api/excel-read-profiles/'+$taskProfileId) $taskUpdate 200
    if(-not $taskSaved.Success -or $taskSaved.Data.Version -ne 2 -or -not $taskSaved.Data.CanRead -or @($taskSaved.Data.Fields).Count -ne 2){throw '原主版本/完整字段保存未通过'}
    $taskFacts.saved_version=2
    $taskFacts.stage='profile-readback'
    $taskRead=Invoke-TaskProfileRequest 'GET' ('/api/excel-read-profiles/'+$taskProfileId) $null 200
    if(-not $taskRead.Success -or $taskRead.Data.Version -ne 2 -or $taskRead.Data.GroupCode -ne $taskGroup -or @($taskRead.Data.Fields).Count -ne 2){throw '持久化读回未通过'}
    $taskOldSnapshot=$taskRead.Data|ConvertTo-Json -Depth 12 -Compress
    $taskUpdate.ConfigName='PG迁移验收20261006-旧版本不应保存'
    $taskFacts.stage='stale-version'
    $taskConflict=Invoke-TaskProfileRequest 'PUT' ('/api/excel-read-profiles/'+$taskProfileId) $taskUpdate 409
    if($taskConflict.Success -or $taskConflict.Error.Code -ne 'VERSION_CONFLICT'){throw '原版本冲突错误契约未通过'}
    $taskFacts.stale_version_error=$taskConflict.Error.Code
    $taskFacts.stage='verify-stale-no-write'
    $taskAfterConflict=Invoke-TaskProfileRequest 'GET' ('/api/excel-read-profiles/'+$taskProfileId) $null 200
    if(-not $taskAfterConflict.Success -or ($taskAfterConflict.Data|ConvertTo-Json -Depth 12 -Compress) -cne $taskOldSnapshot){throw '拒绝后完整持久化DTO改变'}
    $taskFacts.full_snapshot_unchanged_after_rejection=$true
    $taskFacts.persisted_profile=$taskAfterConflict.Data
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
    $taskFacts|ConvertTo-Json -Depth 12|Set-Content -LiteralPath $taskResultPath -Encoding utf8
    $taskFacts|ConvertTo-Json -Depth 12|Write-Output
}
exit $taskExit
