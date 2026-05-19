using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data.Entities;
using Stallions.Shared.Enums;

namespace Stallions.Server.Data.Repositories;

public class UserRepository : IUserRepository
{
    private readonly AppDbContext _db;
    public UserRepository(AppDbContext db) => _db = db;

    public async Task<User?> GetByIdAsync(Guid id) =>
        await _db.Users.FindAsync(id);

    public async Task<User?> GetByEntraObjectIdAsync(string entraObjectId) =>
        await _db.Users.FirstOrDefaultAsync(u => u.EntraObjectId == entraObjectId);

    public async Task<IReadOnlyList<User>> GetAllAsync(UserRole? role = null, UserStatus? status = null)
    {
        var query = _db.Users.AsQueryable();
        if (role.HasValue) query = query.Where(u => u.Role == role.Value);
        if (status.HasValue) query = query.Where(u => u.Status == status.Value);
        return await query.OrderBy(u => u.DisplayName).ToListAsync();
    }

    public async Task<User> AddAsync(User user)
    {
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    public async Task UpdateAsync(User user)
    {
        _db.Users.Update(user);
        await _db.SaveChangesAsync();
    }
}
