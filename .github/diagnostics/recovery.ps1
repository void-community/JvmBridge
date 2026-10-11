$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$diagnostics = [IO.Path]::GetFullPath('artifacts/diagnostics')
$hostExecutable = Join-Path $diagnostics 'host/JavaHost.exe'
$probe = Join-Path $diagnostics 'host/safefetch-probe.dll'
& cl /nologo /O2 /Zi /LD .github/diagnostics/safefetch-probe.c "/Fe:$probe" /link /EXPORT:JvmBridgeDiagnosticInstall=_JvmBridgeDiagnosticInstall /EXPORT:JvmBridgeDiagnosticCount=_JvmBridgeDiagnosticCount
if ($LASTEXITCODE -ne 0) { throw 'Recovery probe compilation failed' }
$inventory = Get-Content src/JvmBridge.Build/jdks.lock.json -Raw | ConvertFrom-Json
$records = [Collections.Generic.List[object]]::new()
$originalPath = $env:PATH
foreach ($major in @(14,18)) {
    $entry = $inventory.jdks | Where-Object { $_.rid -eq 'win-x86' -and $_.java -eq $major -and $_.implementation -eq 'hotspot' }
    $archive = Join-Path $env:RUNNER_TEMP "jdk-$major.zip"
    Invoke-WebRequest $entry.url -OutFile $archive
    if ((Get-FileHash $archive -Algorithm SHA256).Hash -ne $entry.sha256) { throw "JDK $major checksum mismatch" }
    $jdkDirectory = Join-Path $env:RUNNER_TEMP "jdk-$major"
    Expand-Archive $archive $jdkDirectory
    $jvm = Get-ChildItem $jdkDirectory -Filter jvm.dll -Recurse | Select-Object -First 1
    $java = Get-ChildItem $jdkDirectory -Filter java.exe -Recurse | Select-Object -First 1
    $jdkHome = Split-Path $java.DirectoryName
    $env:PATH = $java.DirectoryName + ';' + $originalPath
    & dumpbin /exports $jvm.FullName | Set-Content (Join-Path $diagnostics "java-$major-exports.log")
    $nativeHost = Join-Path $diagnostics "native-$major.exe"
    & cl /nologo /O2 /Zi "/I$jdkHome/include" "/I$jdkHome/include/win32" .github/diagnostics/native-host.c "/Fe:$nativeHost"
    if ($LASTEXITCODE -ne 0) { throw 'C host compilation failed' }
    foreach ($variant in @('baseline','recovery','c-stress')) {
        $env:JVMBRIDGE_DIAGNOSTIC_RECOVERY = if ($variant -eq 'recovery') { '1' } else { '0' }
        $executable = if ($variant -eq 'c-stress') { $nativeHost } else { $hostExecutable }
        $arguments = if ($variant -eq 'c-stress') { @($jvm.FullName, 'stress') } else { @($jvm.FullName) }
        $attemptCount = if ($variant -eq 'c-stress') { 30 } else { 400 }
        $failures = 0
        $recovered = 0
        for ($attempt = 1; $attempt -le $attemptCount; $attempt++) {
            $stdout = Join-Path $diagnostics "java-$major-$variant-$attempt.stdout.log"
            $stderr = Join-Path $diagnostics "java-$major-$variant-$attempt.stderr.log"
            $process = Start-Process $executable -ArgumentList $arguments -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
            $null = $process.Handle
            if (!$process.WaitForExit(60000)) { $process.Kill(); throw "Java $major $variant attempt $attempt timed out" }
            $process.Refresh()
            $code = $process.ExitCode
            $output = Get-Content $stdout -Raw
            $faults = if ($output -match 'SAFEFETCH_RECOVERED=(\d+)') { [int]$matches[1] } else { 0 }
            $recovered += $faults
            $records.Add([pscustomobject]@{ java=$major; mode=$variant; attempt=$attempt; exitCode=$code; recovered=$faults })
            if ($code -ne 0) { $failures++; Write-Host "FAIL Java $major $variant attempt $attempt exit $code" }
            if ($faults -gt 0) { Write-Host "RECOVERED Java $major attempt $attempt count $faults" }
        }
        Write-Host "SUMMARY Java $major $variant failures $failures / $attemptCount recovered $recovered"
        $records | ConvertTo-Json | Set-Content (Join-Path $diagnostics 'attempts.json')
    }
}
$env:PATH = $originalPath
$env:JVMBRIDGE_DIAGNOSTIC_RECOVERY = '0'
