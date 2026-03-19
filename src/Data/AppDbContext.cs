using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using VRChatLotteryTool.Core.Models;

namespace VRChatLotteryTool.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<LotterySession> LotterySessions => Set<LotterySession>();
    public DbSet<LotteryEntry> LotteryEntries => Set<LotteryEntry>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<User>(e =>
        {
            e.HasKey(u => u.UserId);
            e.Property(u => u.DisplayName).IsRequired().HasMaxLength(100);
        });

        mb.Entity<LotterySession>(e =>
        {
            e.HasKey(s => s.SessionId);
            e.Property(s => s.Mode).HasConversion<string>();
            e.Property(s => s.Status).HasConversion<string>();
            e.HasMany(s => s.Entries)
             .WithOne(en => en.Session)
             .HasForeignKey(en => en.SessionId);
        });

        mb.Entity<LotteryEntry>(e =>
        {
            e.HasKey(en => en.EntryId);
            e.Property(en => en.RejectReason).HasMaxLength(200);
            e.Property(en => en.ErrorMessage).HasMaxLength(500);
            e.HasIndex(en => new { en.SessionId, en.UserId }).IsUnique();
        });
    }

    /// <summary>
    /// EnsureCreated の後に呼ぶ。
    /// モデル変更で追加されたカラムが既存DBに存在しない場合に ALTER TABLE で追加する。
    /// EFCore マイグレーション不要の軽量スキーマ更新。
    /// </summary>
    public void ApplyColumnMigrations()
    {
        var conn = Database.GetDbConnection();
        conn.Open();
        try
        {
            // 既存カラム一覧を取得するヘルパー
            HashSet<string> GetColumns(string table)
            {
                var cols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"PRAGMA table_info({table});";
                using var r = cmd.ExecuteReader();
                while (r.Read()) cols.Add(r.GetString(1)); // 1列目 = name
                return cols;
            }

            void AddColumnIfMissing(string table, string column, string definition)
            {
                if (GetColumns(table).Contains(column)) return;
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
                cmd.ExecuteNonQuery();
            }

            // LotteryEntries に NotificationId を追加（TEXT NULL）
            AddColumnIfMissing("LotteryEntries", "NotificationId", "TEXT NULL");

            // 今後カラムを追加する際はここに追記する
            // AddColumnIfMissing("LotteryEntries", "NewColumn", "TEXT NULL");
        }
        finally
        {
            conn.Close();
        }
    }
}
