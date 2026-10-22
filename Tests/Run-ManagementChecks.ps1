param([switch]$Serve)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = & $vswhere -latest -products '*' -find 'MSBuild\Current\Bin\MSBuild.exe' | Select-Object -First 1
$compiler = & $vswhere -latest -products '*' -find 'MSBuild\Current\Bin\Roslyn\csc.exe' | Select-Object -First 1
if (!$msbuild -or !$compiler) { throw '需要 Visual Studio 的 C# 编译工具。' }
& $msbuild "$projectRoot\ToastFish.sln" /p:Configuration=Debug '/p:Platform=Any CPU' '/p:OutputPath=bin\ManagementDebug\' /verbosity:minimal /nologo
if ($LASTEXITCODE -ne 0) { throw '项目编译失败。' }
$output = Join-Path $projectRoot 'bin\ManagementDebug'
$framework = "${env:ProgramFiles(x86)}\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2"
$compilerArgs = @('/nologo', '/target:exe', '/langversion:7.3', "/out:$output\ToastFish.ManagementChecks.exe", "/reference:$output\ToastFish.exe")
foreach ($reference in @('System.dll','System.Core.dll','System.Data.dll','System.Xml.dll','WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll','System.Xaml.dll')) {
    $compilerArgs += "/reference:$framework\$reference"
}
foreach ($reference in @('Dapper.dll','System.Data.SQLite.dll','NPOI.dll','NPOI.OOXML.dll')) { $compilerArgs += "/reference:$output\$reference" }
$compilerArgs += "$PSScriptRoot\ManagementChecks.cs"
& $compiler @compilerArgs
if ($LASTEXITCODE -ne 0) { throw '检查程序编译失败。' }
if ($Serve) { & "$output\ToastFish.ManagementChecks.exe" --serve }
else { & "$output\ToastFish.ManagementChecks.exe" }
if ($LASTEXITCODE -ne 0) { throw '管理功能检查失败。' }
