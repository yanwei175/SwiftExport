# 复用现有逐表保护实现，只插入一个已记录SHA的测试文件，不建立公共框架或修改来源文件。
$ErrorActionPreference='Stop'
Set-Location 'D:\Documents\source\CSharp\SwiftExport'
$taskPath='SwiftExport.Application.Tests/Procurement/PurchaseOrderTrackingSqlTests.cs'
if((Get-FileHash -LiteralPath $taskPath).Hash -ne '930991461B6D0F4F846D88CC3DA33D7F9053799BBD14E3C6AD8727ECA9704AB2'){throw '接续后的原四方法源SHA变化，停止'}
$taskText=Get-Content $taskPath -Raw
$taskHelper=Get-Content 'SwiftExport.Application.Tests/DemandPlanning/MaterialDemandTraceSqlTests.cs' -Raw
$taskStart=$taskHelper.IndexOf('    private static readonly Dictionary<string, (Guid[] Ids, string Snapshot)> OriginalRows')
$taskEnd=$taskHelper.IndexOf('    /// <summary>只为仓储上下文提供测试身份')
if($taskStart -lt 0 -or $taskEnd -le $taskStart){throw '现有保护实现定位不符'}
$taskProtection=$taskHelper.Substring($taskStart,$taskEnd-$taskStart).Replace('p09-s02c-material-trace','p09-s02e-purchase-tracking').Replace('共享收货追踪不能产生库存事实','采购查询跟踪不能产生库存事实')
$taskInsert=$taskText.IndexOf('    /// <summary>只供原四跟踪方法使用的PG夹具')
if($taskInsert -lt 0){throw '本文件插入点不符'}
$taskText=$taskText.Substring(0,$taskInsert)+$taskProtection+$taskText.Substring($taskInsert)
[IO.File]::WriteAllText((Resolve-Path -LiteralPath $taskPath).Path,$taskText,[Text.UTF8Encoding]::new($false))
'Same-file 50-table full-row protection appended; no production or helper-source change.'
