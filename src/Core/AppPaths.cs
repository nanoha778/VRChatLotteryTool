using System.IO;

namespace VRChatLotteryTool.Core;

/// <summary>
/// アプリケーションが使用するファイルパスを一元管理する。
/// すべてのデータは %AppData%\VRChatLotteryTool\ に保存する。
/// DataDir プロパティは呼ぶたびにディレクトリの存在を保証する。
/// </summary>
public static class AppPaths
{
    // プロパティ形式にすることで、初回アクセス時に必ずディレクトリが存在する状態になる
    public static string DataDir
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "VRChatLotteryTool");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string AuthCookie  => Path.Combine(DataDir, ".authcookie");
    public static string Credentials => Path.Combine(DataDir, ".credentials");
    public static string AppSettings => Path.Combine(DataDir, "appsettings.json");
    public static string Database    => Path.Combine(DataDir, "lottery.db");

    /// <summary>起動時に明示的に呼び出してディレクトリ作成を保証する。</summary>
    public static void EnsureCreated() => Directory.CreateDirectory(DataDir);
}
