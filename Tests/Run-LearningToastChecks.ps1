$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$compiler = & $vswhere -latest -products '*' -find 'MSBuild\Current\Bin\Roslyn\csc.exe' | Select-Object -First 1
if (!$compiler) { throw '需要 Visual Studio 的 C# 编译工具。' }
$framework = "${env:ProgramFiles(x86)}\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2"
$toolkit = Join-Path $env:USERPROFILE '.nuget\packages\microsoft.toolkit.uwp.notifications\7.0.1\lib\net461\Microsoft.Toolkit.Uwp.Notifications.dll'
if (!(Test-Path $toolkit)) { throw '请先还原项目的 NuGet 依赖。' }
$output = Join-Path $projectRoot 'bin\LearningToastChecks'
New-Item -ItemType Directory -Force -Path $output | Out-Null
Copy-Item -LiteralPath $toolkit -Destination $output -Force
$compilerArgs = @('/nologo', '/target:exe', '/langversion:7.3', "/out:$output\LearningToastChecks.exe", "/reference:$toolkit")
foreach ($reference in @('System.dll', 'System.Core.dll', 'System.Xml.dll', 'System.Xml.Linq.dll')) {
    $compilerArgs += "/reference:$framework\$reference"
}
$compilerArgs += @("$projectRoot\Model\PushControl\LearningInput.cs",
    "$projectRoot\Model\PushControl\LearningToast.cs", "$PSScriptRoot\LearningToastChecks.cs")
& $compiler @compilerArgs
if ($LASTEXITCODE -ne 0) { throw '自检程序编译失败。' }
& "$output\LearningToastChecks.exe"
if ($LASTEXITCODE -ne 0) { throw '通知自检失败。' }
