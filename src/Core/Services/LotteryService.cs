using VRChatLotteryTool.Core.Models;
using VRChatLotteryTool.Data.Repositories;

namespace VRChatLotteryTool.Core.Services;

public interface ILotteryService
{
    Task<List<LotteryEntry>> DrawAsync(LotterySession session, CancellationToken ct = default);
}

public class LotteryService : ILotteryService
{
    private readonly IWeightCalculator _weightCalculator;
    private readonly IUserRepository _userRepo;
    private readonly ILotteryEntryRepository _entryRepo;
    private readonly ILogService _log;
    private static readonly Random _rng = new();

    public LotteryService(
        IWeightCalculator weightCalculator,
        IUserRepository userRepo,
        ILotteryEntryRepository entryRepo,
        ILogService log)
    {
        _weightCalculator = weightCalculator;
        _userRepo = userRepo;
        _entryRepo = entryRepo;
        _log = log;
    }

    public async Task<List<LotteryEntry>> DrawAsync(LotterySession session, CancellationToken ct = default)
    {
        _log.Info("抽選を開始します...");

        var acceptedEntries = session.Entries.Where(e => e.IsAccepted).ToList();

        if (acceptedEntries.Count == 0)
        {
            _log.Warning("応募者が0人のため抽選をスキップします。");
            return [];
        }

        _log.Info($"応募者数: {acceptedEntries.Count}人 / 当選予定: {session.WinnerCount}人");

        // 全員当選
        if (acceptedEntries.Count <= session.WinnerCount)
        {
            _log.Info("応募者数 ≤ 当選人数のため全員当選とします。");
            foreach (var e in acceptedEntries)
            {
                e.IsWinner = true;
                e.AppliedWeight = 1.0;
            }
            await UpdateUserStats(acceptedEntries, session.Mode, ct);
            return acceptedEntries;
        }

        // 重み付き抽選
        var candidates = acceptedEntries.ToList();
        var winners = new List<LotteryEntry>();

        for (int i = 0; i < session.WinnerCount; i++)
        {
            // 各候補の重み計算
            var weights = new List<(LotteryEntry entry, double weight)>();
            foreach (var entry in candidates)
            {
                var user = await _userRepo.GetOrCreateAsync(entry.UserId, entry.DisplayName, ct);
                var w = _weightCalculator.Calculate(user, session.Mode);
                entry.AppliedWeight = w;
                weights.Add((entry, w));
                _log.Info($"  {entry.DisplayName}: 重み={w:F1}");
            }

            var selected = WeightedRandom(weights);
            selected.IsWinner = true;
            winners.Add(selected);
            candidates.Remove(selected);
            _log.Success($"当選: {selected.DisplayName}");
        }

        // 落選者の重みも保存
        foreach (var entry in candidates)
        {
            var user = await _userRepo.GetOrCreateAsync(entry.UserId, entry.DisplayName, ct);
            entry.AppliedWeight = _weightCalculator.Calculate(user, session.Mode);
        }

        await UpdateUserStats(acceptedEntries, session.Mode, ct);
        return winners;
    }

    private static LotteryEntry WeightedRandom(List<(LotteryEntry entry, double weight)> weighted)
    {
        double total = weighted.Sum(x => x.weight);
        double roll = _rng.NextDouble() * total;
        double cumulative = 0;

        foreach (var (entry, weight) in weighted)
        {
            cumulative += weight;
            if (roll < cumulative) return entry;
        }

        return weighted.Last().entry;
    }

    private async Task UpdateUserStats(List<LotteryEntry> entries, LotteryMode mode, CancellationToken ct)
    {
        foreach (var entry in entries)
        {
            var user = await _userRepo.GetOrCreateAsync(entry.UserId, entry.DisplayName, ct);
            user.TotalEntries++;
            user.LastEntryAt = DateTime.UtcNow;

            if (entry.IsWinner)
            {
                user.TotalWins++;
                user.LoseStreak = 0;
                user.LastWinAt = DateTime.UtcNow;
            }
            else
            {
                user.LoseStreak++;
            }

            user.UpdatedAt = DateTime.UtcNow;
            await _userRepo.UpdateAsync(user, ct);
        }
    }
}
