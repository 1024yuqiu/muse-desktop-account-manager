$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'Build.ps1')
$taskParent = Split-Path -Parent $PSScriptRoot
$taskExe = Join-Path $taskParent 'app\MuseDesktop.exe'
$taskTests = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'tests'))
New-Item -ItemType Directory -Force -Path $taskTests | Out-Null
foreach ($taskMode in @('self', 'smoke')) {
    $taskRun = [System.IO.Path]::GetFullPath((Join-Path $taskTests ($taskMode + '-' + [guid]::NewGuid().ToString('N'))))
    if (-not $taskRun.StartsWith($taskTests + '\', [System.StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid test path' }
    $taskFlag = if ($taskMode -eq 'self') { '--self-test' } else { '--smoke-test' }
    $taskProcess = Start-Process -FilePath $taskExe -ArgumentList @($taskFlag, ('"' + $taskRun + '"')) -WindowStyle Hidden -PassThru
    if (-not $taskProcess.WaitForExit(60000)) {
        $taskProcess.Kill()
        throw 'Client test timed out; test directory retained'
    }
    $taskProcess.Refresh()
    $taskReport = Join-Path $taskRun 'result.txt'
    if (-not (Test-Path -LiteralPath $taskReport)) { throw 'Test report missing; test directory retained' }
    $taskText = Get-Content -LiteralPath $taskReport -Encoding UTF8 -Raw
    Write-Output $taskText
    if ($taskProcess.ExitCode -ne 0 -or -not $taskText.StartsWith('PASS')) { throw 'Client test failed; test directory retained' }
    Copy-Item -LiteralPath $taskReport -Destination (Join-Path $taskTests ($taskMode + '-result.txt')) -Force
    foreach ($taskImageName in @('client.png','client-dark.png','client-collapsed.png','settings.png')) {
        $taskImage = Join-Path $taskRun $taskImageName
        if (Test-Path -LiteralPath $taskImage) { Copy-Item -LiteralPath $taskImage -Destination (Join-Path $taskTests $taskImageName) -Force }
    }
    # Generated, successful test runs only. No production profiles are touched.
    Remove-Item -LiteralPath $taskRun -Recurse -Force
}
