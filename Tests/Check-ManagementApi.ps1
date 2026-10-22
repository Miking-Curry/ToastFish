$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$context = Get-Content (Join-Path $projectRoot 'bin\ManagementDebug\browser-context.json') | ConvertFrom-Json
if ($context.root -notlike '*\ManagementDebug\checks-*') { throw '只允许在隔离测试服务上运行。' }
$apiRoot = 'http://127.0.0.1:17653/api/'
$headers = @{ Authorization = 'Bearer ' + $context.token }
$checkCount = 0
function Check($condition, $name) {
    if (!$condition) { throw "FAIL: $name" }
    $script:checkCount++
}
function Get-Data($path) { Invoke-RestMethod ($apiRoot + $path) -Headers $headers }
function Post-Data($path, $data) {
    Invoke-RestMethod ($apiRoot + $path) -Method Post -Headers $headers -ContentType 'application/json; charset=utf-8' -Body (ConvertTo-Json $data -Depth 20 -Compress)
}
function Status($url, $requestHeaders, $method = 'GET', $body = $null) {
    try {
        $response = Invoke-WebRequest $url -Headers $requestHeaders -Method $method -ContentType 'application/json' -Body $body
        return [int]$response.StatusCode
    } catch { if ($_.Exception.Response) { return [int]$_.Exception.Response.StatusCode }; throw }
}
Check ((Status ($apiRoot + 'dashboard') @{}) -eq 401) 'token required'
Check ((Status ($apiRoot + 'dashboard') @{Authorization=$headers.Authorization;Origin='https://example.invalid'}) -eq 403) 'cross-origin rejected'
Check ((Status ($apiRoot + 'sessions/start') $headers) -eq 404) 'GET cannot mutate'
Check ((Status ($apiRoot + 'words/star') $headers 'POST' '{') -eq 400) 'malformed JSON rejected'
Check ((Status ($apiRoot + 'words/star') $headers 'POST' '{"key":"CET4_1:1"}') -eq 400) 'missing boolean rejected'
Check ((Status 'http://127.0.0.1:17653/assets/..%2F..%2FToastFish.exe' @{}) -in @(403,404,400)) 'path traversal rejected'
$dashboard = Get-Data 'dashboard'
Check ($dashboard.books.Count -ge 2) 'dashboard returns real books'
Check ((Get-Data 'words?book=CET4_1&q=district&page=1').count -eq 1) 'word search'
Check ((Get-Data 'words?book=%27%20OR%201%3D1--&page=1').count -eq 0) 'SQL values parameterized'
$null = Post-Data 'words/star' @{key='CET4_1:1';starred=$false}
Check (!(Get-Data 'words/detail?key=CET4_1%3A1').starred) 'star command'
$settings = Get-Data 'settings'; $settings.Number = 9
$null = Post-Data 'settings' $settings
Check ((Get-Data 'settings').Number -eq 9) 'settings persisted'
$bookFile = Invoke-WebRequest ($apiRoot + 'books/export?book=CET4_1') -Headers $headers
$bytes = $bookFile.RawContentStream.ToArray()
$upload = @{filename='english.xlsx';data=[Convert]::ToBase64String($bytes);name='接口导入词库'}
Check ((Post-Data 'imports/preview' $upload).count -eq 12) 'Excel preview endpoint'
$imported = Post-Data 'imports/confirm' $upload
Check ((Get-Data ('words?book=' + $imported.bookId)).count -eq 12) 'Excel confirmation endpoint'
$backupFile = Invoke-WebRequest ($apiRoot + 'backups') -Headers $headers
$null = Post-Data 'backups/restore' @{data=[Convert]::ToBase64String($backupFile.RawContentStream.ToArray())}
Check ((Get-Data 'settings').Number -eq 9) 'backup restore endpoint'
Check ((Status ($apiRoot + 'backups/restore') $headers 'POST' '{"data":"AQID"}') -eq 400) 'invalid backup endpoint'
$null = Post-Data 'sessions/start' @{bookId='CET4_1';number=1;mode='selected';keys=@('CET4_1:1')}
Check ($null -ne (Get-Data 'sessions')) 'start shared learning engine'
Check ((Status ($apiRoot + 'sessions/start') $headers 'POST' '{"bookId":"CET4_1","number":1,"mode":"normal"}') -eq 409) 'parallel start rejected'
$null = Post-Data 'sessions/pause' @{}
Check ((Get-Data 'sessions').state -eq 'paused') 'pause command'
$null = Post-Data 'sessions/resume' @{}
Check ((Get-Data 'sessions').state -eq 'running') 'resume command'
$null = Post-Data 'sessions/end' @{}
Check ($null -eq (Get-Data 'sessions')) 'end command'
Write-Output "PASS: $checkCount live API checks"
