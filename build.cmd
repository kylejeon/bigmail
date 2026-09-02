@echo off
REM Windows 빌드·배포 스크립트. WPF 는 Windows 에서만 빌드됩니다.
REM 콘솔을 UTF-8 로 — 한글 출력이 깨지지 않게 합니다.
chcp 65001 >nul
setlocal

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
