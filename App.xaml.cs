using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using VRChatLotteryTool.Core;
using VRChatLotteryTool.Core.Models;
using VRChatLotteryTool.Core.Services;
using VRChatLotteryTool.Data;
using VRChatLotteryTool.Data.Repositories;
using VRChatLotteryTool.UI.ViewModels;
using VRChatLotteryTool.Views;

namespace VRChatLotteryTool;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    // Pipeline を設定済みか
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // AppData ディレクトリを起動時に確実に作成
        AppPaths.EnsureCreated();

        // 保存済みテーマを適用
        var savedTheme = AppSettings.Load().Theme;
        // ThemeServiceはDI構築後に取得するため後で適用

        TaskScheduler.UnobservedTaskException += (_, ex) => ex.SetObserved();

        // DI / DB 初期化（同期）
        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();
        db.ApplyColumnMigrations();

        // 保存済みテーマを適用
        Services.GetRequiredService<ThemeService>().Apply(AppSettings.Load().Theme);  // 既存DBへのカラム追加を自動適用

        // MainWindow を即表示 → ログインはウィンドウ内の Loaded で処理
        new MainWindow().Show();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={AppPaths.Database}"));

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<ILotterySessionRepository, LotterySessionRepository>();
        services.AddScoped<ILotteryEntryRepository, LotteryEntryRepository>();

        services.AddSingleton<IVRChatApiClient, VRChatApiClient>();
        services.AddSingleton<ICredentialStore, CredentialStore>();
        services.AddSingleton<ISchedulerService, SchedulerService>();
        services.AddScoped<ILotteryService, LotteryService>();
        services.AddScoped<IWeightCalculator, WeightCalculator>();
        services.AddSingleton<IVRChatNotificationService, VRChatNotificationService>();
        services.AddScoped<IInviteService, InviteService>();
        services.AddSingleton<ILogService, LogService>();
        services.AddSingleton<ISessionStateService, SessionStateService>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<MainViewModel>();
    }
}
