using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Shared.DTOs.Stallions;
using Stallions.Shared.Enums;

namespace Stallions.Server.Services;

public class StallionService : IStallionService
{
    private readonly IStallionRepository _repo;
    private readonly IStudFarmRepository _farmRepo;
    private readonly IUserService _users;

    public StallionService(IStallionRepository repo, IStudFarmRepository farmRepo, IUserService users)
    {
        _repo = repo;
        _farmRepo = farmRepo;
        _users = users;
    }

    public async Task<ServiceResult<IReadOnlyList<StallionSummaryDto>>> GetAllWithActiveListingsAsync()
    {
        var stallions = await _repo.GetWithActiveListingsAsync();
        var dtos = stallions.Select(MapToSummary).ToList();
        return ServiceResult<IReadOnlyList<StallionSummaryDto>>.Ok(dtos);
    }

    public async Task<ServiceResult<IReadOnlyList<StallionSummaryDto>>> GetByStudFarmAsync()
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null) return ServiceResult<IReadOnlyList<StallionSummaryDto>>.Forbidden("Caller identity could not be resolved.");

        var farm = await _farmRepo.GetByUserIdAsync(caller.Id);
        if (farm == null)
            return ServiceResult<IReadOnlyList<StallionSummaryDto>>.NotFound("No stud farm found for the current user.");

        var stallions = await _repo.GetByStudFarmIdAsync(farm.Id);
        var dtos = stallions.Select(MapToSummary).ToList();
        return ServiceResult<IReadOnlyList<StallionSummaryDto>>.Ok(dtos);
    }

    public async Task<ServiceResult<StallionDto>> GetByIdAsync(Guid id, bool isStaff)
    {
        var stallion = await _repo.GetByIdAsync(id);
        if (stallion == null) return ServiceResult<StallionDto>.NotFound("Stallion not found.");
        if (!stallion.IsActive && !isStaff) return ServiceResult<StallionDto>.NotFound("Stallion not found.");
        return ServiceResult<StallionDto>.Ok(MapToDto(stallion));
    }

    public async Task<ServiceResult<StallionDto>> CreateAsync(CreateStallionRequest request)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null) return ServiceResult<StallionDto>.Forbidden("Caller identity could not be resolved.");

        var farm = await _farmRepo.GetByUserIdAsync(caller.Id);
        if (farm == null)
            return ServiceResult<StallionDto>.NotFound("No stud farm found for the current user.");

        var name = request.Name.Trim();
        if (string.IsNullOrEmpty(name))
            return ServiceResult<StallionDto>.BadRequest("Stallion name cannot be empty.");

        var stallion = new Stallion
        {
            StudFarmId = farm.Id,
            Name = name,
            YearOfBirth = request.YearOfBirth,
            Colour = request.Colour,
            Sire = request.Sire,
            Dam = request.Dam,
            RegistrationNumber = request.RegistrationNumber,
            Description = request.Description
        };

        var created = await _repo.AddAsync(stallion);
        return ServiceResult<StallionDto>.Created(MapToDto(created));
    }

    public async Task<ServiceResult<StallionDto>> UpdateAsync(Guid id, UpdateStallionRequest request)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null) return ServiceResult<StallionDto>.Forbidden("Caller identity could not be resolved.");

        var farm = await _farmRepo.GetByUserIdAsync(caller.Id);
        if (farm == null)
            return ServiceResult<StallionDto>.NotFound("No stud farm found for the current user.");

        var stallion = await _repo.GetByIdAsync(id);
        if (stallion == null || !stallion.IsActive)
            return ServiceResult<StallionDto>.NotFound("Stallion not found.");

        if (stallion.StudFarmId != farm.Id)
            return ServiceResult<StallionDto>.Forbidden("You do not have permission to update this stallion.");

        var name = request.Name.Trim();
        if (string.IsNullOrEmpty(name))
            return ServiceResult<StallionDto>.BadRequest("Stallion name cannot be empty.");

        stallion.Name = name;
        stallion.YearOfBirth = request.YearOfBirth;
        stallion.Colour = request.Colour;
        stallion.Sire = request.Sire;
        stallion.Dam = request.Dam;
        stallion.RegistrationNumber = request.RegistrationNumber;
        stallion.Description = request.Description;

        await _repo.UpdateAsync(stallion);
        return ServiceResult<StallionDto>.Ok(MapToDto(stallion));
    }

    public async Task<ServiceResult<StallionDto>> SetPrimaryImageAsync(Guid stallionId, Guid imageId)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null) return ServiceResult<StallionDto>.Forbidden("Caller identity could not be resolved.");

        var farm = await _farmRepo.GetByUserIdAsync(caller.Id);
        if (farm == null)
            return ServiceResult<StallionDto>.NotFound("No stud farm found for the current user.");

        var stallion = await _repo.GetByIdAsync(stallionId);
        if (stallion == null || !stallion.IsActive)
            return ServiceResult<StallionDto>.NotFound("Stallion not found.");

        if (stallion.StudFarmId != farm.Id)
            return ServiceResult<StallionDto>.Forbidden("You do not have permission to update this stallion.");

        var targetImage = stallion.Images.FirstOrDefault(img => img.Id == imageId);
        if (targetImage == null)
            return ServiceResult<StallionDto>.NotFound("Image not found on this stallion.");

        foreach (var img in stallion.Images)
            img.IsPrimary = false;
        targetImage.IsPrimary = true;

        await _repo.UpdateAsync(stallion);
        return ServiceResult<StallionDto>.Ok(MapToDto(stallion));
    }

    public async Task<ServiceResult> DeleteImageAsync(Guid stallionId, Guid imageId)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null) return ServiceResult.Forbidden("Caller identity could not be resolved.");

        var farm = await _farmRepo.GetByUserIdAsync(caller.Id);
        if (farm == null)
            return ServiceResult.NotFound("No stud farm found for the current user.");

        var stallion = await _repo.GetByIdAsync(stallionId);
        if (stallion == null || !stallion.IsActive)
            return ServiceResult.NotFound("Stallion not found.");

        if (stallion.StudFarmId != farm.Id)
            return ServiceResult.Forbidden("You do not have permission to update this stallion.");

        var image = stallion.Images.FirstOrDefault(img => img.Id == imageId);
        if (image == null)
            return ServiceResult.NotFound("Image not found on this stallion.");

        stallion.Images.Remove(image);
        await _repo.UpdateAsync(stallion);
        return ServiceResult.Ok();
    }

    private static StallionSummaryDto MapToSummary(Stallion s) => new()
    {
        Id = s.Id,
        StudFarmId = s.StudFarmId,
        Name = s.Name,
        YearOfBirth = s.YearOfBirth,
        Colour = s.Colour,
        PrimaryImagePath = s.Images.FirstOrDefault(img => img.IsPrimary)?.BlobPath,
        ActiveListingCount = s.Listings.Count(l => l.Status == ListingStatus.Active)
    };

    private static StallionDto MapToDto(Stallion s) => new()
    {
        Id = s.Id,
        StudFarmId = s.StudFarmId,
        Name = s.Name,
        YearOfBirth = s.YearOfBirth,
        Colour = s.Colour,
        Sire = s.Sire,
        Dam = s.Dam,
        RegistrationNumber = s.RegistrationNumber,
        Description = s.Description,
        IsActive = s.IsActive,
        CreatedAt = s.CreatedAt,
        Images = s.Images.Select(img => new StallionImageDto
        {
            Id = img.Id,
            BlobPath = img.BlobPath,
            IsPrimary = img.IsPrimary,
            DisplayOrder = img.DisplayOrder
        }).ToList()
    };
}
