# VRChat Lottery Tool

VRChat の `Request Invite` を自動受付・抽選・Invite 送信する .NET 10 WPF デスクトップアプリです。

---

## 必要環境

| 項目 | バージョン |
|------|-----------|
| Visual Studio | 2022 以降 (2026) |
| .NET SDK | 10.0 |
| OS | Windows 10/11 |

---

## セットアップ

```bash
# NuGet パッケージ復元 (Visual Studio が自動実行)
dotnet restore

# ビルド
dotnet build

# 実行
dotnet run
```

初回起動時に `lottery.db`（SQLite）がアプリと同じディレクトリに自動生成されます。

---

## ディレクトリ構成

```
VRChatLotteryTool/
├── VRChatLotteryTool.csproj
├── App.xaml / App.xaml.cs           # アプリエントリーポイント・DI設定
│
├── src/
│   ├── Core/
│   │   ├── Models/
│   │   │   ├── User.cs              # ユーザー統計モデル
│   │   │   ├── LotterySession.cs    # セッション・ステータス・モード定義
│   │   │   ├── LotteryEntry.cs      # 応募単位モデル
│   │   │   ├── AppSettings.cs       # 設定モデル
│   │   │   └── LogEntry.cs          # ログエントリモデル
│   │   │
│   │   └── Services/
│   │       ├── WeightCalculator.cs       # 重み付き抽選計算（公平/救済）
│   │       ├── LotteryService.cs         # 抽選実行ロジック
│   │       ├── LogService.cs             # ログ管理
│   │       ├── SessionStateService.cs    # セッション状態管理
│   │       ├── SchedulerService.cs       # 時刻スケジューラ
│   │       ├── VRChatNotificationService.cs  # VRChat通知受信（要実装）
│   │       └── InviteService.cs          # Invite送信（要実装）
│   │
│   ├── Data/
│   │   ├── AppDbContext.cs           # EF Core DbContext
│   │   └── Repositories/
│   │       └── Repositories.cs       # User / Session / Entry リポジトリ
│   │
│   └── UI/
│       ├── ViewModels/
│       │   └── MainViewModel.cs      # メイン ViewModel (MVVM)
│       ├── Views/
│       │   ├── MainWindow.xaml       # メインウィンドウ UI
│       │   ├── MainWindow.xaml.cs    # コードビハインド
│       │   └── Styles.xaml           # 共通スタイル
│       └── Converters/
│           └── Converters.cs         # IValueConverter 群
│
└── resources/                        # アイコン等リソース
```

---

## VRChat 連携の実装について

本アプリには **スタブ実装** が含まれており、以下の2ファイルを実際の VRChat API に合わせて実装してください。

### `VRChatNotificationService.cs`
Request Invite の受信処理です。VRChat の通知取得方法（OSC / ログ監視 / 非公式API等）に応じて `RequestInviteReceived` イベントを発火してください。

```csharp
// 受信時にこのイベントを発火する
RequestInviteReceived?.Invoke(this, new RequestInviteEventArgs
{
    UserId = "usr_xxxx",
    DisplayName = "UserName",
    ReceivedAt = DateTime.Now
});
```

### `InviteService.cs`
当選者への Invite 送信処理です。`SendInviteAsync` メソッド内に VRChat API 呼び出しを実装してください。

---

## 抽選ロジック

### 公平モード
```
重み = 100 - 直近当選ペナルティ(15) - 累計当選数 × 2 (上限30)
```

### 救済モード
```
重み = 100 + 連続落選数 × 10 (上限100) + 未当選ボーナス(30)
           - 直近当選ペナルティ(25) - 累計当選数 × 5 (上限50)
```

重みの最小値は `0.1` で、0以下にはなりません。

---

## テスト用機能

UI の「🧪 テスト送信 (Simulate)」ボタンを押すと、ランダムなユーザーIDで Request Invite イベントを発火できます。VRChat に接続せずに動作確認が可能です。

---

## 後回し候補 (将来実装)

- ブラックリスト / ホワイトリスト
- 同日再当選禁止
- 重みパラメータのUI編集
- 抽選シミュレーション
- Invite 送信リトライ
- Discord 通知連携
