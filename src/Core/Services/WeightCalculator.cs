using VRChatLotteryTool.Core.Models;

namespace VRChatLotteryTool.Core.Services;

public interface IWeightCalculator
{
    double Calculate(User user, LotteryMode mode);
}

public class WeightCalculator : IWeightCalculator
{
    // 公平モード定数
    private const double FairBase = 100.0;
    private const double FairRecentWinPenalty = 15.0;   // 直近当選ペナルティ
    private const double FairTotalWinReduction = 2.0;    // 累計当選ごとの軽減

    // 救済モード定数
    private const double ReliefBase = 100.0;
    private const double ReliefLoseStreakBonus = 10.0;   // 連続落選ボーナス(回数倍率)
    private const double ReliefNeverWonBonus = 30.0;     // 一度も当選していないボーナス
    private const double ReliefRecentWinPenalty = 25.0;  // 直近当選ペナルティ
    private const double ReliefTotalWinPenalty = 5.0;    // 累計当選ごとのペナルティ

    // 直近当選とみなす時間(時間)
    private const double RecentWinHours = 24.0;

    public double Calculate(User user, LotteryMode mode)
    {
        double weight = mode == LotteryMode.Fair
            ? CalculateFair(user)
            : CalculateRelief(user);

        // 重みは最小0.1を保証
        return Math.Max(0.1, weight);
    }

    private static double CalculateFair(User user)
    {
        double w = FairBase;

        // 直近当選ペナルティ
        if (IsRecentWinner(user))
            w -= FairRecentWinPenalty;

        // 累計当選軽減(上限30)
        w -= Math.Min(user.TotalWins * FairTotalWinReduction, 30.0);

        return w;
    }

    private static double CalculateRelief(User user)
    {
        double w = ReliefBase;

        // 連続落選ボーナス(上限100)
        w += Math.Min(user.LoseStreak * ReliefLoseStreakBonus, 100.0);

        // 一度も当選していないボーナス
        if (user.TotalWins == 0)
            w += ReliefNeverWonBonus;

        // 直近当選ペナルティ
        if (IsRecentWinner(user))
            w -= ReliefRecentWinPenalty;

        // 累計当選ペナルティ(上限50)
        w -= Math.Min(user.TotalWins * ReliefTotalWinPenalty, 50.0);

        return w;
    }

    private static bool IsRecentWinner(User user)
    {
        if (user.LastWinAt == null) return false;
        return (DateTime.UtcNow - user.LastWinAt.Value).TotalHours < RecentWinHours;
    }
}
