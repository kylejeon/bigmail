@echo off
REM Windows 빌드·배포 스크립트. WPF 는 Windows 에서만 빌드됩니다.
chcp 65001 >nul
setlocal

REM ---- 사전 점검: dotnet 이 있는가 ----
where dotnet >nul 2>nul
if errorlevel 1 goto :no_dotnet

echo == 0/3  환경 확인 ==
dotnet --version
if errorlevel 1 goto :no_dotnet
echo.
echo 설치된 SDK:
dotnet --list-sdks
echo.

echo == 1/3  테스트 (WPF 제외 전 계층) ==
dotnet test JlkMailer.NoWpf.slnf -c Release
if errorlevel 1 goto :failed_tests

echo.
echo == 2/3  WPF 앱 단일 exe 배포 ==
dotnet publish src\JlkMailer.App\JlkMailer.App.csproj -c Release -o publish\app
if errorlevel 1 goto :failed_wpf

echo.
echo == 3/3  CLI 배포 ==
dotnet publish src\JlkMailer.Cli\JlkMailer.Cli.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish\cli
if errorlevel 1 goto :failed_cli

echo.
echo 완료
echo   publish\app\JlkMailer.exe   (WPF 앱)
echo   publish\cli\jlkmail.exe     (CLI)
goto :eof

:no_dotnet
echo.
echo [중단] .NET SDK 를 찾을 수 없습니다. 빌드 실패가 아니라 개발 도구가 없는 상태입니다.
echo.
echo 설치 방법 (둘 중 하나):
echo.
echo   1) winget  ^(권장, 관리자 권한 불필요^)
echo        winget install Microsoft.DotNet.SDK.8
echo.
echo   2) 설치 관리자 내려받기
echo        https://dotnet.microsoft.com/ko-kr/download/dotnet/8.0
echo        "SDK" 의 x64 설치 관리자를 받으세요. "런타임" 이 아닙니다.
echo.
echo 설치 후에는 이 터미널을 닫고 새로 여세요. PATH 가 갱신되지 않습니다.
echo VS Code 를 쓰신다면 VS Code 자체를 다시 시작해야 합니다.
echo.
echo 이미 설치했는데도 이 메시지가 보인다면 터미널 재시작 문제입니다.
exit /b 1

:failed_tests
echo.
echo [실패] 테스트가 깨졌습니다. 위 출력에서 실패한 테스트 이름을 확인하세요.
exit /b 1

:failed_wpf
echo.
echo [실패] WPF 빌드가 깨졌습니다.
echo   XAML 컴파일 오류는 macOS 에서 검증할 수 없어 여기서 처음 드러납니다.
echo   오류 전문을 보려면:
echo     dotnet build src\JlkMailer.App\JlkMailer.App.csproj -c Debug -v normal
echo   로직 계층은 이미 테스트를 통과했으므로 원인은 XAML 또는 코드비하인드입니다.
exit /b 1

:failed_cli
echo.
echo [실패] CLI 빌드가 깨졌습니다.
exit /b 1
