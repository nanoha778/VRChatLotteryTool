using Microsoft.EntityFrameworkCore;
using VRChatLotteryTool.Core.Models;
using VRChatLotteryTool.Data;

namespace VRChatLotteryTool.Data.Repositories;

// -----------------------------------------------------------------------
// User
// -----------------------------------------------------------------------
public interface IUserRepository
{
    Task<User?> GetAsync(string userId, CancellationToken ct = default);
    Task<User> GetOrCreateAsync(string userId, string displayName, CancellationToken ct = default);
    Task UpdateAsync(User user, CancellationToken ct = default);
}

public class UserRepository : IUserRepository
{
    private readonly AppDbContext _db;
    public UserRepository(AppDbContext db) => _db = db;

    public Task<User?> GetAsync(string userId, CancellationToken ct)
        => _db.Users.FirstOrDefaultAsync(u => u.UserId == userId, ct);

    public async Task<User> GetOrCreateAsync(string userId, string displayName, CancellationToken ct)
    {
        var user = await GetAsync(userId, ct);
        if (user != null) return user;

        user = new User { UserId = userId, DisplayName = displayName };
        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);
        return user;
    }

    public async Task UpdateAsync(User user, CancellationToken ct)
    {
        _db.Users.Update(user);
        await _db.SaveChangesAsync(ct);
    }
}

// -----------------------------------------------------------------------
// LotterySession
// -----------------------------------------------------------------------
public interface ILotterySessionRepository
{
    Task<LotterySession> CreateAsync(LotterySession session, CancellationToken ct = default);
    Task<LotterySession?> GetWithEntriesAsync(string sessionId, CancellationToken ct = default);
    Task UpdateAsync(LotterySession session, CancellationToken ct = default);
}

public class LotterySessionRepository : ILotterySessionRepository
{
    private readonly AppDbContext _db;
    public LotterySessionRepository(AppDbContext db) => _db = db;

    public async Task<LotterySession> CreateAsync(LotterySession session, CancellationToken ct)
    {
        _db.LotterySessions.Add(session);
        await _db.SaveChangesAsync(ct);
        return session;
    }

    public Task<LotterySession?> GetWithEntriesAsync(string sessionId, CancellationToken ct)
        => _db.LotterySessions
              .Include(s => s.Entries)
              .FirstOrDefaultAsync(s => s.SessionId == sessionId, ct);

    public async Task UpdateAsync(LotterySession session, CancellationToken ct)
    {
        _db.LotterySessions.Update(session);
        await _db.SaveChangesAsync(ct);
    }
}

// -----------------------------------------------------------------------
// LotteryEntry
// -----------------------------------------------------------------------
public interface ILotteryEntryRepository
{
    Task<LotteryEntry> AddAsync(LotteryEntry entry, CancellationToken ct = default);
    Task<LotteryEntry?> FindDuplicateAsync(string sessionId, string userId, CancellationToken ct = default);
    Task UpdateAsync(LotteryEntry entry, CancellationToken ct = default);
    Task UpdateRangeAsync(IEnumerable<LotteryEntry> entries, CancellationToken ct = default);
}

public class LotteryEntryRepository : ILotteryEntryRepository
{
    private readonly AppDbContext _db;
    public LotteryEntryRepository(AppDbContext db) => _db = db;

    public async Task<LotteryEntry> AddAsync(LotteryEntry entry, CancellationToken ct)
    {
        _db.LotteryEntries.Add(entry);
        await _db.SaveChangesAsync(ct);
        return entry;
    }

    public Task<LotteryEntry?> FindDuplicateAsync(string sessionId, string userId, CancellationToken ct)
        => _db.LotteryEntries.FirstOrDefaultAsync(
               e => e.SessionId == sessionId && e.UserId == userId, ct);

    public async Task UpdateAsync(LotteryEntry entry, CancellationToken ct)
    {
        _db.LotteryEntries.Update(entry);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateRangeAsync(IEnumerable<LotteryEntry> entries, CancellationToken ct)
    {
        _db.LotteryEntries.UpdateRange(entries);
        await _db.SaveChangesAsync(ct);
    }
}
