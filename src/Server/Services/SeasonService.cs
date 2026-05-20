using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Shared.DTOs.Seasons;

namespace Stallions.Server.Services;

public class SeasonService : ISeasonService
{
    private readonly ISeasonRepository _repo;
    private readonly IUserService _users;

    public SeasonService(ISeasonRepository repo, IUserService users)
    { _repo = repo; _users = users; }

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
        if (request.EndDate <= request.StartDate)
            return ServiceResult<SeasonDto>.BadRequest("End date must be after start date.");
        var season = new Season
        {
            Name = request.Name.Trim(),
            StartDate = request.StartDate,
            EndDate = request.EndDate
        };
        var created = await _repo.AddAsync(season);
        return ServiceResult<SeasonDto>.Created(MapToDto(created));
    }

    public async Task<ServiceResult<SeasonDto>> UpdateAsync(Guid id, UpdateSeasonRequest request)
    {
        var season = await _repo.GetByIdAsync(id);
        if (season == null) return ServiceResult<SeasonDto>.NotFound();
        if (request.EndDate <= request.StartDate)
            return ServiceResult<SeasonDto>.BadRequest("End date must be after start date.");
        season.Name = request.Name.Trim();
        season.StartDate = request.StartDate;
        season.EndDate = request.EndDate;
        await _repo.UpdateAsync(season);
        return ServiceResult<SeasonDto>.Ok(MapToDto(season));
    }

    public async Task<ServiceResult> OpenSeasonAsync(Guid id)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null) return ServiceResult.Forbidden();
        var season = await _repo.GetByIdAsync(id);
        if (season == null) return ServiceResult.NotFound("Season not found.");
        var alreadyOpen = await _repo.GetCurrentOpenSeasonAsync();
        if (alreadyOpen != null && alreadyOpen.Id != id)
            return ServiceResult.BadRequest("Another season is already open. Close it first.");
        season.IsOpen = true;
        season.OpenedAt = DateTime.UtcNow;
        season.OpenedByUserId = caller.Id;
        await _repo.UpdateAsync(season);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> CloseSeasonAsync(Guid id)
    {
        await _users.GetOrCreateCurrentUserAsync();
        var season = await _repo.GetByIdAsync(id);
        if (season == null) return ServiceResult.NotFound("Season not found.");
        season.IsOpen = false;
        await _repo.UpdateAsync(season);
        return ServiceResult.Ok();
    }

    private static SeasonDto MapToDto(Season s) => new()
    {
        Id = s.Id, Name = s.Name, StartDate = s.StartDate, EndDate = s.EndDate,
        IsOpen = s.IsOpen, OpenedAt = s.OpenedAt, CreatedAt = s.CreatedAt
    };
}
