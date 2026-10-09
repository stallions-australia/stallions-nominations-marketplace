using System.Text.Json;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Shared.DTOs.Seasons;
using Stallions.Shared.Enums;

namespace Stallions.Server.Services;

public class SeasonService : ISeasonService
{
    private readonly ISeasonRepository _repo;
    private readonly IUserService _users;
    private readonly IAuditLogRepository _auditRepo;

    public SeasonService(ISeasonRepository repo, IUserService users, IAuditLogRepository auditRepo)
    { _repo = repo; _users = users; _auditRepo = auditRepo; }

    private const string StaffOnlyMessage = "Only Stallions Australia staff can manage seasons.";

    // Controllers enforce the StaffOnly policy; checked again here (defence in depth).
    private async Task<User?> GetStaffCallerAsync()
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        return caller?.Role == UserRole.Staff ? caller : null;
    }

    private static object Snapshot(Season s) => new
    {
        s.Name,
        StartDate = s.StartDate.ToString("yyyy-MM-dd"),
        EndDate = s.EndDate.ToString("yyyy-MM-dd")
    };

    public async Task<ServiceResult<IReadOnlyList<SeasonDto>>> GetAllAsync()
    {
        var seasons = await _repo.GetAllAsync();
        return ServiceResult<IReadOnlyList<SeasonDto>>.Ok(seasons.Select(MapToDto).ToList());
    }

    public async Task<ServiceResult<SeasonDto>> GetCurrentAsync()
    {
        var season = await _repo.GetCurrentOpenSeasonAsync();
        return season == null
            ? ServiceResult<SeasonDto>.NotFound("No season is currently open.")
            : ServiceResult<SeasonDto>.Ok(MapToDto(season));
    }

    public async Task<ServiceResult<SeasonDto>> CreateAsync(CreateSeasonRequest request)
    {
        var staff = await GetStaffCallerAsync();
        if (staff == null) return ServiceResult<SeasonDto>.Forbidden(StaffOnlyMessage);

        var name = request.Name.Trim();
        if (string.IsNullOrEmpty(name))
            return ServiceResult<SeasonDto>.BadRequest("Season name cannot be empty.");
        if (request.EndDate <= request.StartDate)
            return ServiceResult<SeasonDto>.BadRequest("End date must be after start date.");
        var season = new Season
        {
            Name = name,
            StartDate = request.StartDate,
            EndDate = request.EndDate
        };
        var created = await _repo.AddAsync(season);
        await _auditRepo.LogAsync("Season", created.Id, "CreateSeason", staff.Id,
            JsonSerializer.Serialize(Snapshot(created)));
        return ServiceResult<SeasonDto>.Created(MapToDto(created));
    }

    public async Task<ServiceResult<SeasonDto>> UpdateAsync(Guid id, UpdateSeasonRequest request)
    {
        var staff = await GetStaffCallerAsync();
        if (staff == null) return ServiceResult<SeasonDto>.Forbidden(StaffOnlyMessage);

        var name = request.Name.Trim();
        if (string.IsNullOrEmpty(name))
            return ServiceResult<SeasonDto>.BadRequest("Season name cannot be empty.");
        var season = await _repo.GetByIdAsync(id);
        if (season == null) return ServiceResult<SeasonDto>.NotFound();
        if (request.EndDate <= request.StartDate)
            return ServiceResult<SeasonDto>.BadRequest("End date must be after start date.");
        var old = Snapshot(season);
        season.Name = name;
        season.StartDate = request.StartDate;
        season.EndDate = request.EndDate;
        await _repo.UpdateAsync(season);
        await _auditRepo.LogAsync("Season", season.Id, "UpdateSeason", staff.Id,
            JsonSerializer.Serialize(new { Old = old, New = Snapshot(season) }));
        return ServiceResult<SeasonDto>.Ok(MapToDto(season));
    }

    public async Task<ServiceResult> OpenSeasonAsync(Guid id)
    {
        var caller = await GetStaffCallerAsync();
        if (caller == null) return ServiceResult.Forbidden(StaffOnlyMessage);
        var season = await _repo.GetByIdAsync(id);
        if (season == null) return ServiceResult.NotFound("Season not found.");
        var alreadyOpen = await _repo.GetCurrentOpenSeasonAsync();
        if (alreadyOpen != null && alreadyOpen.Id != id)
            return ServiceResult.BadRequest("Another season is already open. Close it first.");
        season.IsOpen = true;
        season.OpenedAt = DateTime.UtcNow;
        season.OpenedByUserId = caller.Id;
        await _repo.UpdateAsync(season);
        await _auditRepo.LogAsync("Season", season.Id, "OpenSeason", caller.Id,
            JsonSerializer.Serialize(Snapshot(season)));
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> CloseSeasonAsync(Guid id)
    {
        var caller = await GetStaffCallerAsync();
        if (caller == null) return ServiceResult.Forbidden(StaffOnlyMessage);
        var season = await _repo.GetByIdAsync(id);
        if (season == null) return ServiceResult.NotFound("Season not found.");
        season.IsOpen = false;
        await _repo.UpdateAsync(season);
        await _auditRepo.LogAsync("Season", season.Id, "CloseSeason", caller.Id,
            JsonSerializer.Serialize(Snapshot(season)));
        return ServiceResult.Ok();
    }

    private static SeasonDto MapToDto(Season s) => new()
    {
        Id = s.Id, Name = s.Name, StartDate = s.StartDate, EndDate = s.EndDate,
        IsOpen = s.IsOpen, OpenedAt = s.OpenedAt, CreatedAt = s.CreatedAt
    };
}
