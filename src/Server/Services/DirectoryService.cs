using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Shared.DTOs.Directory;

namespace Stallions.Server.Services;

public class DirectoryService : IDirectoryService
{
    private readonly IStudDirectoryRepository _studRepo;
    private readonly IStallionDirectoryRepository _stallionRepo;

    public DirectoryService(
        IStudDirectoryRepository studRepo,
        IStallionDirectoryRepository stallionRepo)
    {
        _studRepo = studRepo;
        _stallionRepo = stallionRepo;
    }

    // ── Stud Directory ────────────────────────────────────────────────────────

    public async Task<ServiceResult<IReadOnlyList<StudDirectorySummaryDto>>> GetStudDirectoriesAsync(
        bool includeInactive = false)
    {
        var entries = await _studRepo.GetAllAsync(includeInactive);
        var dtos = entries.Select(MapToStudSummary).ToList();
        return ServiceResult<IReadOnlyList<StudDirectorySummaryDto>>.Ok(dtos);
    }

    public async Task<ServiceResult<StudDirectoryDto>> GetStudDirectoryAsync(Guid id)
    {
        var entry = await _studRepo.GetByIdAsync(id);
        if (entry == null)
            return ServiceResult<StudDirectoryDto>.NotFound("Stud directory entry not found.");
        return ServiceResult<StudDirectoryDto>.Ok(MapToStudDto(entry));
    }

    public async Task<ServiceResult<StudDirectoryDto>> CreateStudDirectoryAsync(
        CreateStudDirectoryRequest request)
    {
        var name = request.Name.Trim();
        if (string.IsNullOrEmpty(name))
            return ServiceResult<StudDirectoryDto>.BadRequest("Name is required.");

        var entry = new StudDirectory
        {
            Name = name,
            ArionStudId = request.ArionStudId,
            Website = request.Website,
            Address = request.Address,
            Town = request.Town,
            State = request.State,
            Country = request.Country,
            Phone = request.Phone,
            Email = request.Email,
            LogoUrl = request.LogoUrl,
            IsActive = request.IsActive
        };

        var created = await _studRepo.AddAsync(entry);
        return ServiceResult<StudDirectoryDto>.Created(MapToStudDto(created));
    }

    public async Task<ServiceResult<StudDirectoryDto>> UpdateStudDirectoryAsync(
        Guid id, UpdateStudDirectoryRequest request)
    {
        var entry = await _studRepo.GetByIdAsync(id);
        if (entry == null)
            return ServiceResult<StudDirectoryDto>.NotFound("Stud directory entry not found.");

        var name = request.Name.Trim();
        if (string.IsNullOrEmpty(name))
            return ServiceResult<StudDirectoryDto>.BadRequest("Name is required.");

        entry.Name = name;
        entry.ArionStudId = request.ArionStudId;
        entry.Website = request.Website;
        entry.Address = request.Address;
        entry.Town = request.Town;
        entry.State = request.State;
        entry.Country = request.Country;
        entry.Phone = request.Phone;
        entry.Email = request.Email;
        entry.LogoUrl = request.LogoUrl;
        entry.IsActive = request.IsActive;

        await _studRepo.UpdateAsync(entry);
        return ServiceResult<StudDirectoryDto>.Ok(MapToStudDto(entry));
    }

    // ── Stallion Directory ───────────────────────────────────────────────────

    public async Task<ServiceResult<IReadOnlyList<StallionDirectorySummaryDto>>> GetStallionDirectoriesAsync(
        bool includeInactive = false, Guid? studDirectoryId = null)
    {
        var entries = await _stallionRepo.GetAllAsync(includeInactive, studDirectoryId);
        var dtos = entries.Select(MapToStallionSummary).ToList();
        return ServiceResult<IReadOnlyList<StallionDirectorySummaryDto>>.Ok(dtos);
    }

    public async Task<ServiceResult<StallionDirectoryDto>> GetStallionDirectoryAsync(Guid id)
    {
        var entry = await _stallionRepo.GetByIdAsync(id);
        if (entry == null)
            return ServiceResult<StallionDirectoryDto>.NotFound("Stallion directory entry not found.");
        return ServiceResult<StallionDirectoryDto>.Ok(MapToStallionDto(entry));
    }

    public async Task<ServiceResult<StallionDirectoryDto>> CreateStallionDirectoryAsync(
        CreateStallionDirectoryRequest request)
    {
        var stud = await _studRepo.GetByIdAsync(request.StudDirectoryId);
        if (stud == null)
            return ServiceResult<StallionDirectoryDto>.NotFound("Stud directory entry not found.");

        var name = request.Name.Trim();
        if (string.IsNullOrEmpty(name))
            return ServiceResult<StallionDirectoryDto>.BadRequest("Name is required.");

        var entry = new StallionDirectory
        {
            StudDirectoryId = request.StudDirectoryId,
            StallionId = request.StallionId,
            ArionId = request.ArionId,
            Name = name,
            YearOfBirth = request.YearOfBirth,
            Colour = request.Colour,
            Height = request.Height,
            SireName = request.SireName,
            DamName = request.DamName,
            IsActive = request.IsActive,
            StudDirectory = stud
        };

        var created = await _stallionRepo.AddAsync(entry);
        return ServiceResult<StallionDirectoryDto>.Created(MapToStallionDto(created));
    }

    public async Task<ServiceResult<StallionDirectoryDto>> UpdateStallionDirectoryAsync(
        Guid id, UpdateStallionDirectoryRequest request)
    {
        var entry = await _stallionRepo.GetByIdAsync(id);
        if (entry == null)
            return ServiceResult<StallionDirectoryDto>.NotFound("Stallion directory entry not found.");

        // Validate the target stud exists if it changed
        if (entry.StudDirectoryId != request.StudDirectoryId)
        {
            var stud = await _studRepo.GetByIdAsync(request.StudDirectoryId);
            if (stud == null)
                return ServiceResult<StallionDirectoryDto>.NotFound("Target stud directory entry not found.");
        }

        var name = request.Name.Trim();
        if (string.IsNullOrEmpty(name))
            return ServiceResult<StallionDirectoryDto>.BadRequest("Name is required.");

        entry.StudDirectoryId = request.StudDirectoryId;
        entry.StallionId = request.StallionId;
        entry.ArionId = request.ArionId;
        entry.Name = name;
        entry.YearOfBirth = request.YearOfBirth;
        entry.Colour = request.Colour;
        entry.Height = request.Height;
        entry.SireName = request.SireName;
        entry.DamName = request.DamName;
        entry.IsActive = request.IsActive;

        await _stallionRepo.UpdateAsync(entry);
        return ServiceResult<StallionDirectoryDto>.Ok(MapToStallionDto(entry));
    }

    // ── Mapping ──────────────────────────────────────────────────────────────

    private static StudDirectorySummaryDto MapToStudSummary(StudDirectory d) => new()
    {
        Id = d.Id,
        ArionStudId = d.ArionStudId,
        Name = d.Name,
        State = d.State,
        Website = d.Website,
        IsActive = d.IsActive,
        StallionCount = d.Stallions?.Count ?? 0
    };

    private static StudDirectoryDto MapToStudDto(StudDirectory d) => new()
    {
        Id = d.Id,
        ArionStudId = d.ArionStudId,
        Name = d.Name,
        Website = d.Website,
        Address = d.Address,
        Town = d.Town,
        State = d.State,
        Country = d.Country,
        Phone = d.Phone,
        Email = d.Email,
        LogoUrl = d.LogoUrl,
        IsActive = d.IsActive,
        CreatedAt = d.CreatedAt,
        UpdatedAt = d.UpdatedAt,
        Stallions = d.Stallions.Select(MapToStallionSummary).ToList()
    };

    private static StallionDirectorySummaryDto MapToStallionSummary(StallionDirectory d) => new()
    {
        Id = d.Id,
        StallionId = d.StallionId,
        ArionId = d.ArionId,
        StudDirectoryId = d.StudDirectoryId,
        StudName = d.StudDirectory?.Name ?? string.Empty,
        Name = d.Name,
        YearOfBirth = d.YearOfBirth,
        Colour = d.Colour,
        IsActive = d.IsActive
    };

    private static StallionDirectoryDto MapToStallionDto(StallionDirectory d) => new()
    {
        Id = d.Id,
        StallionId = d.StallionId,
        ArionId = d.ArionId,
        StudDirectoryId = d.StudDirectoryId,
        StudName = d.StudDirectory?.Name ?? string.Empty,
        Name = d.Name,
        YearOfBirth = d.YearOfBirth,
        Colour = d.Colour,
        Height = d.Height,
        SireName = d.SireName,
        DamName = d.DamName,
        IsActive = d.IsActive,
        CreatedAt = d.CreatedAt,
        UpdatedAt = d.UpdatedAt
    };
}
