using System.IO;
using System.Text.Json;
using JlkMailer.Infrastructure.Security;
using JlkMailer.Presentation.Services;

namespace JlkMailer.App.Services;

/// <summary>
/// 설계 §04: 자격증명은 Windows DPAPI(CurrentUser)로 암호화해 저장한다.
/// 파일에는 암호문만 들어가며, 다른 사용자 계정이나 다른 PC 에서는 복호화되지 않는다.
/// </summary>
public sealed class SecretService : ISecretService
{
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "JlkMailer", "credentials.json");

    public bool CanPersist => SecretStore.IsSupported;

    public void Save(string account, string secret)
    {
        if (!CanPersist) return;

        var map = Load();
        map[account] = SecretStore.ProtectOrThrow(secret);
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        File.WriteAllText(StorePath, JsonSerializer.Serialize(map));
    }

    public string? Load(string account)
    {
        if (!CanPersist) return null;
        if (!Load().TryGetValue(account, out var protectedValue)) return null;

        try { return SecretStore.UnprotectOrThrow(protectedValue); }
        catch { return null; }   // 다른 계정/PC 에서 만든 암호문이면 복호화되지 않는다. 조용히 무시한다.
    }

    public void Clear(string account)
    {
        var map = Load();
        if (!map.Remove(account)) return;
        File.WriteAllText(StorePath, JsonSerializer.Serialize(map));
    }

    private static Dictionary<string, string> Load()
    {
        if (!File.Exists(StorePath)) return [];
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(StorePath)) ?? [];
        }
        catch { return []; }
    }
}
