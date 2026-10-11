namespace Stallions.Server.Data.Entities;

/// <summary>
/// Rows that background jobs and users may update at the same time. AppDbContext gives the stamp
/// a new value on every update, and EF rejects an update whose stamp changed since it was read
/// (DbUpdateConcurrencyException) — so two app instances can't both close an auction or charge
/// a purchase.
/// </summary>
public interface IHasConcurrencyStamp
{
    Guid ConcurrencyStamp { get; set; }
}
