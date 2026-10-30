﻿# CNC-MES Automated Build, Versioning and Deployment Packaging Script
param(
    [string]$Version = "1.3.9"
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$mesRoot = Split-Path -Parent $scriptDir
$serverProject = Join-Path $mesRoot "src\CNCMes.Server\CNCMes.Server.csproj"
$clientProject = Join-Path $mesRoot "src\CNCMes.Client\CNCMes.Client.csproj"
$desktopPath = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::Desktop)
$publishRoot = Join-Path $mesRoot "publish\CNCMes_v$Version"
$serverPubDir = Join-Path $publishRoot "1_중앙_서버_실행"
$clientPubDir = Join-Path $publishRoot "2_현장_작업자_클라이언트"
$zipFileName = "CNC_MES_v${Version}_배포.zip"
$zipFullPath = Join-Path $desktopPath $zipFileName

Write-Host "======================================================================"
Write-Host " [CNC-MES] Server and Client Auto Packaging (v$Version)"
Write-Host "======================================================================"

# 1. Clean previous publish folder
if (Test-Path $publishRoot) {
    cmd /c "rmdir /s /q `"$publishRoot`""
}
New-Item -ItemType Directory -Force -Path $serverPubDir | Out-Null
New-Item -ItemType Directory -Force -Path $clientPubDir | Out-Null

# 2. Publish Server (Self-Contained win-x64 Single File)
Write-Host "[1/4] Publishing Server (Self-Contained win-x64 Single File)..."
dotnet publish $serverProject -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $serverPubDir --nologo

# 3. Publish Client (Self-Contained win-x64 Single File)
Write-Host "[2/4] Publishing Client (Self-Contained win-x64 Single File)..."
dotnet publish $clientProject -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $clientPubDir --nologo

# 4. Generate Launcher Scripts (CRITICAL: BOM-less UTF-8 for CMD compatibility)
Write-Host "[3/4] Generating launcher batch files (BOM-less UTF-8)..."
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

# Server Bat (Auto-elevates to Admin, disables firewall, opens Port 5000 & 5002, and starts Server!)
$serverBat = @"
@echo off
cd /d "%~dp0"
title [CNC-MES Server v$Version]

:: Auto-elevate to Administrator so Firewall & Port 5000 are guaranteed open
net session >nul 2>&1
if %errorlevel% neq 0 (
    powershell -Command "Start-Process cmd -ArgumentList '/c `"%~f0`"' -Verb RunAs"
    exit /b
)

echo ======================================================================
echo  [CNC-MES] Configuring Offline LAN Firewall and Starting Server v$Version
echo ======================================================================
powershell -Command "Get-NetConnectionProfile | Set-NetConnectionProfile -NetworkCategory Private -ErrorAction SilentlyContinue" >nul 2>&1
netsh advfirewall set allprofiles state off >nul 2>&1
netsh advfirewall firewall add rule name="CNC-MES Port 5000" dir=in action=allow protocol=TCP localport=5000 enable=yes >nul 2>&1
netsh advfirewall firewall add rule name="CNC-MES Discovery Port 5002" dir=in action=allow protocol=UDP localport=5002 enable=yes >nul 2>&1
netsh advfirewall firewall add rule name="CNC-MES Server Exe" dir=in action=allow program="%~dp0CNCMes.Server.exe" enable=yes >nul 2>&1

:: Web dashboard is automatically opened by CNCMes.Server once started (single tab)

"%~dp0CNCMes.Server.exe"
pause
"@
[System.IO.File]::WriteAllText((Join-Path $serverPubDir "run_server.bat"), $serverBat, $utf8NoBom)
[System.IO.File]::WriteAllText((Join-Path $serverPubDir "서버실행.bat"), $serverBat, $utf8NoBom)

# Server Firewall Killer & Auto-Admin Bat
$serverFwBat = @"
@echo off
cd /d "%~dp0"
title [CNC-MES Server Firewall Setup]
echo ======================================================================
echo  [CNC-MES] Server Firewall Bypass and Port Open (Port 5000 / 5002)
echo ======================================================================
echo.

net session >nul 2>&1
if %errorlevel% neq 0 (
    echo [*] Administrator privileges required. Elevating...
    powershell -Command "Start-Process cmd -ArgumentList '/c `"%~f0`"' -Verb RunAs"
    exit /b
)

echo [1] Setting Network Profile to Private (Local LAN)...
powershell -Command "Get-NetConnectionProfile | Set-NetConnectionProfile -NetworkCategory Private -ErrorAction SilentlyContinue"

echo [2] Disabling Windows Firewall for Closed Offline LAN...
netsh advfirewall set allprofiles state off

echo [3] Adding Firewall Inbound Rules (Port 5000, 5002 UDP Discovery)...
netsh advfirewall firewall add rule name="CNC-MES Port 5000" dir=in action=allow protocol=TCP localport=5000 enable=yes
netsh advfirewall firewall add rule name="CNC-MES Discovery Port 5002" dir=in action=allow protocol=UDP localport=5002 enable=yes
netsh advfirewall firewall add rule name="CNC-MES Server Exe" dir=in action=allow program="%~dp0CNCMes.Server.exe" enable=yes

echo.
echo ======================================================================
echo  [SUCCESS] Server Firewall has been configured for offline LAN!
echo ======================================================================
echo.
pause
"@
[System.IO.File]::WriteAllText((Join-Path $serverPubDir "[관리자권한]서버_방화벽_해제.bat"), $serverFwBat, $utf8NoBom)
[System.IO.File]::WriteAllText((Join-Path $serverPubDir "allow_firewall_port5000.bat"), $serverFwBat, $utf8NoBom)

# Client Bat
$clientBat = "@echo off`r`ncd /d `"%~dp0`"`r`ntitle [CNC-MES Client v$Version]`r`nstart `"`" `"%~dp0CNCMes.Client.exe`"`r`n"
[System.IO.File]::WriteAllText((Join-Path $clientPubDir "run_client.bat"), $clientBat, $utf8NoBom)
[System.IO.File]::WriteAllText((Join-Path $clientPubDir "클라이언트실행.bat"), $clientBat, $utf8NoBom)

# Client Firewall Killer & Auto-Admin Bat
$clientFwBat = @"
@echo off
cd /d "%~dp0"
title [CNC-MES Client Firewall Setup]
echo ======================================================================
echo  [CNC-MES] Worker Client Firewall Bypass for Closed LAN
echo ======================================================================
echo.

net session >nul 2>&1
if %errorlevel% neq 0 (
    echo [*] Administrator privileges required. Elevating...
    powershell -Command "Start-Process cmd -ArgumentList '/c `"%~f0`"' -Verb RunAs"
    exit /b
)

echo [1] Setting Network Profile to Private (Local LAN)...
powershell -Command "Get-NetConnectionProfile | Set-NetConnectionProfile -NetworkCategory Private -ErrorAction SilentlyContinue"

echo [2] Disabling Windows Firewall for Closed Offline LAN...
netsh advfirewall set allprofiles state off

echo [3] Adding Firewall Rules for Client...
netsh advfirewall firewall add rule name="CNC-MES Client Exe Out" dir=out action=allow program="%~dp0CNCMes.Client.exe" enable=yes
netsh advfirewall firewall add rule name="CNC-MES Client Exe In" dir=in action=allow program="%~dp0CNCMes.Client.exe" enable=yes

echo.
echo ======================================================================
echo  [SUCCESS] Client PC Firewall has been completely disabled!
echo ======================================================================
echo.
pause
"@
[System.IO.File]::WriteAllText((Join-Path $clientPubDir "[관리자권한]클라이언트_방화벽_해제.bat"), $clientFwBat, $utf8NoBom)

$clientCfg = "{`r`n  `"ServerUrl`": `"http://localhost:5000`",`r`n  `"LastUsername`": `"`",`r`n  `"LastMachineId`": `"R01`"`r`n}`r`n"
[System.IO.File]::WriteAllText((Join-Path $clientPubDir "client_config.json"), $clientCfg, $utf8NoBom)

# README (in UTF-8 No BOM)
$readmeText = @"
======================================================================
 CNC 통합 가공관리 시스템 (CNC-MES v$Version) - 폐쇄망/공유폴더 배포팩
======================================================================

[ 필수 주의사항 ]
1. 반드시 ZIP 압축 파일을 [압축 풀기]로 해제한 후 폴더 안에서 실행하십시오.
2. 인터넷이 일체 연결되지 않는 오프라인 폐쇄망 환경에서도 100% 정상 작동합니다.
3. 바탕화면\제조3팀공유폴더 -진행중 폴더에 작지 폴더를 넣으면 시스템에 자동 등록됩니다.
4. 가공 완료 시 자동으로 99-완료백업 폴더로 이동 보관되어 언제든 다시 열람할 수 있습니다.

----------------------------------------------------------------------
[ 1. 중앙 서버 PC (1_중앙_서버_실행 폴더) ]
----------------------------------------------------------------------
1. 'run_server.bat' (또는 '서버실행.bat') 더블클릭 실행
   -> 자동으로 관리자 권한을 획득하고 방화벽을 해제한 뒤 서버를 시작합니다.
   -> 서버가 시작되면 자동으로 '2_현장_작업자_클라이언트' 폴더의 설정 파일에
      현재 서버의 폐쇄망 IP가 자동 동기화됩니다!

----------------------------------------------------------------------
[ 2. 현장 작업자 PC (2_현장_작업자_클라이언트 폴더) ]
----------------------------------------------------------------------
1. 공유폴더로 접속하거나, '2_현장_작업자_클라이언트' 폴더를 작업자 PC로 복사합니다.
2. 'run_client.bat' (또는 'CNCMes.Client.exe') 더블클릭 실행
   -> 서버를 브로드캐스트/ARP/UDP/공유폴더 자동 탐색하여 바로 자동 접속됩니다!
"@
[System.IO.File]::WriteAllText((Join-Path $publishRoot "README.txt"), $readmeText, $utf8NoBom)

# 5. Clean older zip archives on Desktop
Write-Host "[4/5] Cleaning previous CNC_MES_v*_배포.zip on Desktop..."
Get-ChildItem -Path $desktopPath -Filter "CNC_MES_v*_배포.zip" | ForEach-Object {
    Write-Host " Removing old archive: $($_.Name)"
    Remove-Item -Force $_.FullName
}

# 6. Create Desktop Zip archive
Write-Host "[5/5] Creating deployment zip archive on Desktop: $zipFullPath"
Compress-Archive -Path "$publishRoot\*" -DestinationPath $zipFullPath -Force

$zipSize = (Get-Item $zipFullPath).Length
$zipSizeMb = [Math]::Round(($zipSize / 1MB), 2)
Write-Host "======================================================================"
Write-Host " [SUCCESS] Deployment archive created on Desktop!"
Write-Host " File: $zipFullPath"
Write-Host " Size: $zipSizeMb MB"
Write-Host "======================================================================"
