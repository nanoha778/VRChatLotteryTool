using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VRChatLotteryTool.Core;

namespace VRChatLotteryTool.Core.Services;

/// <summary>
/// ログイン情報をローカルに暗号化して保存・読み込みするサービス。
/// Windows DPAPI (ProtectedData) を使用するため、同一ユーザー・同一マシンでのみ復号可能。
/// </summary>
public interface ICredentialStore
{
    void Save(string username, string password);
    (string username, string password)? Load();
    void Clear();
}

public class CredentialStore : ICredentialStore
{
    private static readonly string FilePath = AppPaths.Credentials;

    public void Save(string username, string password)
    {
        var plain   = JsonSerializer.Serialize(new { username, password });
        var bytes   = Encoding.UTF8.GetBytes(plain);
        var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(FilePath, encrypted);
    }

    public (string username, string password)? Load()
    {
        if (!File.Exists(FilePath)) return null;
        try
        {
            var encrypted = File.ReadAllBytes(FilePath);
            var bytes     = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            var plain     = Encoding.UTF8.GetString(bytes);
            var doc       = JsonSerializer.Deserialize<JsonElement>(plain);
            return (doc.GetProperty("username").GetString() ?? "",
                    doc.GetProperty("password").GetString() ?? "");
        }
        catch
        {
            // 復号失敗時はファイルを削除してクリアに
            Clear();
            return null;
        }
    }

    public void Clear()
    {
        if (File.Exists(FilePath))
            File.Delete(FilePath);
    }
}
