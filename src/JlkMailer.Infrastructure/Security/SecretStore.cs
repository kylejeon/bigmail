using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace JlkMailer.Infrastructure.Security;

/// <summary>
/// 설계 §04: 자격증명은 Windows DPAPI(CurrentUser)로 암호화해 저장하고 설정 파일에 평문으로 두지 않는다.
/// Windows 외 환경(개발·테스트)에서는 DPAPI 가 없으므로 저장을 거부한다 — 조용히 평문으로 떨어뜨리면 안 된다.
/// </summary>
public static class SecretStore
{
    public static bool IsSupported => OperatingSystem.IsWindows();

    [SupportedOSPlatform("windows")]
    public static string Protect(string plaintext)
    {
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plaintext), null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(bytes);
    }

    [SupportedOSPlatform("windows")]
    public static string Unprotect(string protectedBase64)
    {
        var bytes = ProtectedData.Unprotect(Convert.FromBase64String(protectedBase64), null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(bytes);
    }

    // OperatingSystem.IsWindows() 를 직접 부르는 것이 중요하다.
    // 분석기가 이 호출만 플랫폼 가드로 인식하므로, IsSupported 프로퍼티로 감싸면 CA1416 이 뜬다.
    public static string ProtectOrThrow(string plaintext)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                "자격증명 암호화(DPAPI)는 Windows 에서만 지원됩니다. 이 환경에서는 비밀번호를 저장할 수 없습니다.");
        return Protect(plaintext);
    }

    public static string UnprotectOrThrow(string protectedBase64)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                "자격증명 복호화(DPAPI)는 Windows 에서만 지원됩니다.");
        return Unprotect(protectedBase64);
    }
}
