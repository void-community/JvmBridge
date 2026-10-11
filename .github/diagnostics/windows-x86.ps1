$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$diagnostics = [IO.Path]::GetFullPath('artifacts/diagnostics')
$dumpDirectory = Join-Path $diagnostics 'dumps'
New-Item -ItemType Directory -Force $dumpDirectory | Out-Null
$debugger = "${env:ProgramFiles(x86)}\Windows Kits\10\Debuggers\x86\cdb.exe"
if (!(Test-Path $debugger)) {
    $setup = Join-Path $env:RUNNER_TEMP 'winsdksetup.exe'
    Invoke-WebRequest 'https://download.microsoft.com/download/2/6/f/26f7aa55-ef6f-4882-b19b-a1be0e7328fe/KIT_BUNDLE_WINDOWSSDK_MEDIACREATION/winsdksetup.exe' -OutFile $setup
    if ((Get-FileHash $setup -Algorithm SHA256).Hash -ne '5535188A9AEEA1CEBCBF04DE3C2C37D76F10600A65867FF65F6153D507B60488') { throw 'SDK installer checksum mismatch' }
    $installer = Start-Process $setup -ArgumentList '/quiet /norestart /features OptionId.WindowsDesktopDebuggers' -Wait -PassThru
    if ($installer.ExitCode -notin 0,3010) { throw "SDK debugger installer exited $($installer.ExitCode)" }
}
if (!(Test-Path $debugger)) { throw 'Windows native debugger unavailable' }
foreach ($registry in @('HKLM:\SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\JavaHost.exe', 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\Windows Error Reporting\LocalDumps\JavaHost.exe')) {
    New-Item $registry -Force | Out-Null
    New-ItemProperty $registry -Name DumpFolder -PropertyType ExpandString -Value $dumpDirectory -Force | Out-Null
    New-ItemProperty $registry -Name DumpType -PropertyType DWord -Value 2 -Force | Out-Null
    New-ItemProperty $registry -Name DumpCount -PropertyType DWord -Value 4 -Force | Out-Null
}
$hostExecutable = Join-Path $diagnostics 'host/JavaHost.exe'
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
    $env:PATH = $java.DirectoryName + ';' + $originalPath
    $failures = 0
    for ($attempt = 1; $attempt -le 10 -and $failures -lt 3; $attempt++) {
        $stdout = Join-Path $diagnostics "java-$major-normal-$attempt.stdout.log"
        $stderr = Join-Path $diagnostics "java-$major-normal-$attempt.stderr.log"
        $process = Start-Process $hostExecutable -ArgumentList @($jvm.FullName, '0') -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
        $null = $process.Handle
        if (!$process.WaitForExit(60000)) { $process.Kill(); throw "Java $major attempt $attempt timed out" }
        $process.Refresh()
        $code = $process.ExitCode
        $records.Add([pscustomobject]@{ java=$major; mode='normal'; attempt=$attempt; exitCode=$code })
        Write-Host "Java $major ordinary attempt $attempt exit $code"
        if ($code -ne 0) { $failures++; Get-Content $stdout; Get-Content $stderr }
    }
    for ($attempt = 1; $attempt -le 400; $attempt++) {
        $dump = Join-Path $dumpDirectory "java-$major-debug-$attempt.dmp"
        $log = Join-Path $diagnostics "java-$major-debug-$attempt.log"
        $capture = ".echo SECOND_CHANCE_CRASH; .exr -1; .ecxr; r; kv; ~* kb; lm; u @eip-20 L30; .dump /ma $dump; q"
        $termination = ".if (poi(@esp+8) == 0xc0000005) { .echo CRASH_TERMINATION; $capture } .else { gc }"
        $commands = "sxn ld; sxd -c2 `"$capture`" av; sxd -c2 `"$capture`" 0xc0000409; sxd -c2 `"$capture`" 0xe0434352; bu ntdll!NtTerminateProcess `"$termination`"; g"
        & $debugger -hd -g -G -y (Join-Path $diagnostics 'host') -logo $log -c $commands $hostExecutable $jvm.FullName 0
        $records.Add([pscustomobject]@{ java=$major; mode='debug'; attempt=$attempt; exitCode=$LASTEXITCODE; dump=(Test-Path $dump) })
        if (Test-Path $dump) { break }
    }
}
$env:PATH = $originalPath
$records | ConvertTo-Json | Set-Content (Join-Path $diagnostics 'attempts.json')
foreach ($dump in Get-ChildItem $dumpDirectory -Filter '*.dmp') {
    & $debugger -z $dump.FullName -y (Join-Path $diagnostics 'host') -logo (Join-Path $diagnostics ($dump.BaseName + '-analysis.log')) -c '.exr -1; .ecxr; r; kv; ~* kb; lm; !address @eip; !address @esp; u @eip-20 L30; dps @esp L80; q'
}
