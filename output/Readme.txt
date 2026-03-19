VRChat Lottery Tool - Readme

このツールは、VRChatの「Request Invite」を自動で受付し、抽選してInviteを送信するアプリです。

【動作環境】
・Windows 10 / 11
・.NET 10 Desktop Runtime
・VRChatアカウント（2FA対応）

【起動方法】

1. zipを展開
2. VRChatLotteryTool.exe を実行

【初回起動】
・VRChatのログイン情報を入力してください
・2段階認証が有効な場合はコード入力が必要です
・ログイン情報は保存可能です

【基本的な使い方】

1. 当選人数・時間などを設定
2. セッション開始を押す
3. Request Inviteを自動受付
4. 指定時間に抽選＆Invite送信

※「今すぐ抽選」で手動実行も可能

【抽選モード】
・公平：ほぼランダム
・救済：当選していない人を優遇

【注意事項】
・VRChatのワールドに入室していないとInvite送信できません
・WebSocket接続が切れている間はRequest Inviteを受信できません
・テスト送信では実際のInviteは送信されません

【データ保存先】
%AppData%\VRChatLotteryTool\

・設定ファイル
・ログイン情報
・抽選履歴 などが保存されます

【よくあるトラブル】
・Inviteが送れない → ワールドに入室しているか確認
・ログインできない → 2FAコードを確認
・起動しない → .NET Runtimeを確認

【アンインストール】
フォルダ削除でOK
必要に応じてAppData内も削除してください

---

VRChat Lottery Tool

©nanoha_778