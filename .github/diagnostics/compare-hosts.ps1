$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$diagnostics = [IO.Path]::GetFullPath('artifacts/diagnostics')
dotnet publish samples/JavaHost -c Release -r win-x86 --self-contained -p:PublishAot=false -p:JvmBridgeVersion=$env:PACKAGE_VERSION -o artifacts/diagnostics/coreclr
if ($LASTEXITCODE -ne 0) { throw 'CoreCLR host publish failed' }
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
    $nativeHost = Join-Path $diagnostics "native-$major.exe"
    & cl /nologo /O2 /Zi "/I$jdkHome/include" "/I$jdkHome/include/win32" .github/diagnostics/native-host.c "/Fe:$nativeHost"
    if ($LASTEXITCODE -ne 0) { throw 'C host compilation failed' }
    foreach ($variant in @('aot','coreclr','c')) {
        $executable = switch ($variant) {
            'aot' { Join-Path $diagnostics 'host/JavaHost.exe' }
            'coreclr' { Join-Path $diagnostics 'coreclr/JavaHost.exe' }
            'c' { $nativeHost }
        }
        $failures = 0
        for ($attempt = 1; $attempt -le 150; $attempt++) {
            $stdout = Join-Path $diagnostics "java-$major-$variant-$attempt.stdout.log"
            $stderr = Join-Path $diagnostics "java-$major-$variant-$attempt.stderr.log"
            $process = Start-Process $executable -ArgumentList @($jvm.FullName) -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
            $null = $process.Handle
            if (!$process.WaitForExit(60000)) { $process.Kill(); throw "Java $major $variant attempt $attempt timed out" }
            $process.Refresh()
            $code = $process.ExitCode
            $records.Add([pscustomobject]@{ java=$major; mode=$variant; attempt=$attempt; exitCode=$code })
            if ($code -ne 0) { $failures++; Write-Host "FAIL Java $major $variant attempt $attempt exit $code"; Get-Content $stdout; Get-Content $stderr }
        }
        Write-Host "SUMMARY Java $major $variant failures $failures / 150"
        $records | ConvertTo-Json | Set-Content (Join-Path $diagnostics 'attempts.json')
    }
}
$env:PATH = $originalPath
Get-ChildItem -Path . -Filter 'hs_err_pid*' | Copy-Item -Destination $diagnostics
