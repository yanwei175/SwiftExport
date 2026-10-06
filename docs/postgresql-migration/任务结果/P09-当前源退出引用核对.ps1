# 主控源退出预检：只读源码、项目和现有包图；输出名称、路径、行号与哈希，不读取数据库或配置凭据。
$ErrorActionPreference='Stop'
Set-Location 'D:\Documents\source\CSharp\SwiftExport'
$taskReportPath='docs/postgresql-migration/任务结果/P09-源实现退出只读审查-A.md'
$taskOldLines=Get-Content -LiteralPath $taskReportPath
$taskStart=($taskOldLines | Select-String '^## 旧实现消费者全清单').LineNumber
$taskEnd=($taskOldLines | Select-String '^## 最小串行移除方案').LineNumber
$taskNames=@($taskOldLines[($taskStart-1)..($taskEnd-2)] | ForEach-Object { if($_ -match '^\| ([A-Za-z][A-Za-z0-9_]*) \|'){$Matches[1]} })
if($taskNames.Count -ne 74){throw '历史74项候选范围已变化，先检查审查入口。'}
$taskNames=@($taskNames + @('SqlServerSchemaGenerator','PermissionCatalogSync','SqlAdminPasswordRecoveryStore') | Sort-Object -Unique)
$taskPaths=@(rg --files --no-ignore -g '*.cs' -g '*.csproj' -g '!弃用资料/**' -g '!.git/**' -g '!.codex/**' -g '!.agents/**' -g '!**/obj/**' -g '!**/bin/**')
$taskSources=@{}
$taskHashes=@{}
foreach($taskPath in $taskPaths){
    $taskNormalized=$taskPath.Replace('\','/')
    $taskSources[$taskNormalized]=[IO.File]::ReadAllText((Join-Path (Get-Location) $taskPath))
    $taskHashes[$taskNormalized]=(Get-FileHash -LiteralPath $taskPath -Algorithm SHA256).Hash
}
$taskDefinitions=@{}
foreach($taskPath in $taskSources.Keys | Where-Object { $_.EndsWith('.cs') }){
    foreach($taskMatch in [regex]::Matches($taskSources[$taskPath],'(?m)^\s*(?:public|internal)\s+(?:(?:sealed|static|abstract|partial)\s+)*class\s+([A-Za-z_][A-Za-z0-9_]*)')){
        $taskName=$taskMatch.Groups[1].Value
        if($taskNames -ccontains $taskName){
            if(-not $taskDefinitions.ContainsKey($taskName)){$taskDefinitions[$taskName]=@()}
            $taskDefinitions[$taskName]+= $taskPath
        }
    }
}
$taskRows=@()
foreach($taskName in $taskNames){
    $taskReferences=@()
    $taskPattern='(?<![A-Za-z0-9_])'+[regex]::Escape($taskName)+'(?![A-Za-z0-9_])'
    foreach($taskPath in $taskSources.Keys | Where-Object { $_.EndsWith('.cs') } | Sort-Object){
        if(@($taskDefinitions[$taskName]) -ccontains $taskPath){continue}
        $taskSource=$taskSources[$taskPath]
        foreach($taskMatch in [regex]::Matches($taskSource,$taskPattern)){
            $taskLine=1+([regex]::Matches($taskSource.Substring(0,$taskMatch.Index),'\n')).Count
            $taskArea=if($taskPath -match '(^|/)[^/]*Tests/'){ '测试' }elseif($taskPath.StartsWith('_tools/')){'工具'}elseif($taskPath -match '(^|/)PostgreSql[^/]*\.cs$'){'PG实现'}else{'其他源码'}
            $taskReferences+=[pscustomobject]@{path=$taskPath;line=$taskLine;area=$taskArea}
        }
    }
    $taskRows+=[pscustomobject]@{name=$taskName;definitions=@($taskDefinitions[$taskName] | Sort-Object -Unique);references=$taskReferences}
}
$taskPackages=@()
$taskAssets=@()
foreach($taskPath in $taskSources.Keys | Where-Object { $_.EndsWith('.csproj') } | Sort-Object){
    [xml]$taskProject=$taskSources[$taskPath]
    foreach($taskPackage in $taskProject.SelectNodes('//PackageReference')){
        if($taskPackage.GetAttribute('Include') -eq 'Microsoft.Data.SqlClient'){
            $taskPackages+=[pscustomobject]@{project=$taskPath;version=$taskPackage.GetAttribute('Version')}
        }
    }
    $taskAssetPath=Join-Path (Split-Path $taskPath) 'obj/project.assets.json'
    if(Test-Path -LiteralPath $taskAssetPath){
        $taskAsset=Get-Content -LiteralPath $taskAssetPath -Raw | ConvertFrom-Json -AsHashtable
        $taskSqlPackages=@($taskAsset.libraries.Keys | Where-Object { $_ -cmatch '^Microsoft\.Data\.SqlClient/' })
        $taskAssets+=[pscustomobject]@{project=$taskPath;sql_client=$taskSqlPackages;sha256=(Get-FileHash -LiteralPath $taskAssetPath -Algorithm SHA256).Hash}
    }
}
$taskEvidence=[ordered]@{recorded_at=(Get-Date).ToString('o');scope='现行源码和项目，排除弃用资料/.git/.agents/.codex/bin/obj；obj中仅显式读取现有project.assets.json';method='大小写敏感完整标识符文字检索，保留注释与字符串命中，非编译器符号或动态调用证明';source_count=$taskSources.Count;candidate_count=$taskRows.Count;candidates=$taskRows;direct_sql_client_packages=$taskPackages;existing_asset_graphs=$taskAssets;source_hashes=$taskHashes}
$taskEvidence | ConvertTo-Json -Depth 9 | Set-Content -LiteralPath 'docs/postgresql-migration/任务结果/P09-当前源退出引用事实.json' -Encoding utf8
[pscustomobject]@{source_count=$taskSources.Count;candidate_count=$taskRows.Count;defined_candidates=@($taskRows | Where-Object {$_.definitions.Count -gt 0}).Count;pg_text_hits=@($taskRows | Where-Object {@($_.references | Where-Object area -eq 'PG实现').Count -gt 0} | Select-Object -ExpandProperty name);direct_packages=$taskPackages;assets_with_sql=@($taskAssets | Where-Object {$_.sql_client.Count -gt 0} | Select-Object -ExpandProperty project)} | ConvertTo-Json -Depth 5 -Compress

