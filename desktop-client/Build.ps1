$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $taskCompiler)) { throw '.NET Framework compiler is unavailable' }
$taskVersion = '1.0.4258.31'
$taskPackage = Join-Path $taskRoot 'vendor\webview2'
if (-not (Test-Path -LiteralPath "$taskPackage\lib\net462\Microsoft.Web.WebView2.Core.dll")) {
    New-Item -ItemType Directory -Force -Path (Join-Path $taskRoot 'vendor') | Out-Null
    $taskZip = Join-Path $taskRoot 'vendor\webview2.zip'
    Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/$taskVersion/microsoft.web.webview2.$taskVersion.nupkg" -OutFile $taskZip
    Expand-Archive -LiteralPath $taskZip -DestinationPath $taskPackage -Force
}
$taskBin = Join-Path (Split-Path -Parent $taskRoot) 'app'
New-Item -ItemType Directory -Force -Path $taskBin | Out-Null
Copy-Item -LiteralPath "$taskPackage\lib\net462\Microsoft.Web.WebView2.Core.dll","$taskPackage\lib\net462\Microsoft.Web.WebView2.WinForms.dll","$taskPackage\runtimes\win-x64\native\WebView2Loader.dll" -Destination $taskBin -Force
Copy-Item -LiteralPath "$taskPackage\LICENSE.txt","$taskPackage\NOTICE.txt" -Destination $taskBin -Force
$taskReferences = @('/r:System.dll','/r:System.Core.dll','/r:System.Drawing.dll','/r:System.Windows.Forms.dll','/r:System.Web.Extensions.dll',"/r:$taskBin\Microsoft.Web.WebView2.Core.dll","/r:$taskBin\Microsoft.Web.WebView2.WinForms.dll")
$taskSources = Get-ChildItem -LiteralPath (Join-Path $taskRoot 'src') -Filter '*.cs' | Select-Object -ExpandProperty FullName
if (-not (Test-Path -LiteralPath "$taskRoot\assets\app.ico")) { & (Join-Path $taskRoot 'BuildIcon.ps1') }
& $taskCompiler /nologo /target:winexe /platform:x64 /optimize+ /utf8output "/out:$taskBin\MuseDesktop.exe" "/win32manifest:$taskRoot\src\app.manifest" "/win32icon:$taskRoot\assets\app.ico" @taskReferences @taskSources
if ($LASTEXITCODE -ne 0) { throw 'Client build failed' }
Write-Output 'Build OK: MuseDesktop.exe'
