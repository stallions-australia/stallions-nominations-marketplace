using Microsoft.AspNetCore.Http;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Shared.DTOs.Directory;
using Stallions.Shared.DTOs.Stallions;
using Stallions.Shared.Enums;

namespace Stallions.Server.Services;

public class StallionService : IStallionService
{
    private readonly IStallionRepository _repo;
    private readonly IStudFarmRepository _farmRepo;
    private readonly IStallionDirectoryRepository _directoryRepo;
    private readonly IUserService _users;
    private readonly IBlobStorageService _blobs;

    public StallionService(
        IStallionRepository repo,
        IStudFarmRepository farmRepo,
        IStallionDirectoryRepository directoryRepo,
        IUserService users,
        IBlobStorageService blobs)
    {
        _repo = repo;
        _farmRepo = farmRepo;
        _directoryRepo = directoryRepo;
        _users = users;
        _blobs = blobs;
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

        // Directory-managed farms must use AddFromDirectoryAsync instead.
        if (farm.StudDirectoryId.HasValue)
            return ServiceResult<StallionDto>.Forbidden(
                "This farm is directory-managed. Use the 'Add from directory' flow to add stallions.");

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
        // Allow editing inactive stallions (needed for reactivation).
        // Only reject if the stallion doesn't exist at all.
        if (stallion == null)
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

        if (request.IsActive.HasValue)
            stallion.IsActive = request.IsActive.Value;

        await _repo.UpdateAsync(stallion);
        return ServiceResult<StallionDto>.Ok(MapToDto(stallion));
    }

    public async Task<ServiceResult<StallionDto>> UploadImageAsync(Guid stallionId, IFormFile file)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null) return ServiceResult<StallionDto>.Forbidden("Caller identity could not be resolved.");

        var farm = await _farmRepo.GetByUserIdAsync(caller.Id);
        if (farm == null)
            return ServiceResult<StallionDto>.NotFound("No stud farm found for the current user.");

        var stallion = await _repo.GetByIdAsync(stallionId);
        if (stallion == null)
            return ServiceResult<StallionDto>.NotFound("Stallion not found.");

        if (stallion.StudFarmId != farm.Id)
            return ServiceResult<StallionDto>.Forbidden("You do not have permission to upload images for this stallion.");

        var allowedTypes = new[] { "image/jpeg", "image/png", "image/webp" };
        if (!allowedTypes.Contains(file.ContentType.ToLowerInvariant()))
            return ServiceResult<StallionDto>.BadRequest("Only JPEG, PNG and WebP images are accepted.");

        if (file.Length > 10 * 1024 * 1024)
            return ServiceResult<StallionDto>.BadRequest("Image must be smaller than 10 MB.");

        await using var stream = file.OpenReadStream();
        var blobUrl = await _blobs.UploadStallionImageAsync(
            stallionId, file.FileName, stream, file.ContentType);

        // If this is the first image, mark it primary automatically.
        var isPrimary = !stallion.Images.Any();

        var image = new StallionImage
        {
            StallionId = stallionId,
            BlobPath = blobUrl,
            IsPrimary = isPrimary,
            DisplayOrder = stallion.Images.Count
        };
        stallion.Images.Add(image);
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

    public async Task<ServiceResult<AuthorizedStallionsDto>> GetAuthorizedAsync()
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null)
            return ServiceResult<AuthorizedStallionsDto>.Forbidden("Caller identity could not be resolved.");

        var farm = await _farmRepo.GetByUserIdAsync(caller.Id);
        if (farm == null)
            return ServiceResult<AuthorizedStallionsDto>.NotFound("No stud farm found for the current user.");

        if (!farm.StudDirectoryId.HasValue)
        {
            return ServiceResult<AuthorizedStallionsDto>.Ok(new AuthorizedStallionsDto
            {
                IsLinked = false,
                FarmName = farm.Name
            });
        }

        var dirEntries = await _directoryRepo.GetByStudDirectoryIdAsync(farm.StudDirectoryId.Value, activeOnly: true);
        var existingStallions = await _repo.GetByStudFarmIdAsync(farm.Id);

        var addedDirectoryIds = existingStallions
            .Where(s => s.StallionDirectoryId.HasValue)
            .Select(s => s.StallionDirectoryId!.Value)
            .ToHashSet();

        var available = dirEntries
            .Where(d => !addedDirectoryIds.Contains(d.Id))
            .Select(d => new StallionDirectorySummaryDto
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
            })
            .ToList();

        return ServiceResult<AuthorizedStallionsDto>.Ok(new AuthorizedStallionsDto
        {
            IsLinked = true,
            FarmName = farm.Name,
            Available = available
        });
    }

    public async Task<ServiceResult<StallionDto>> AddFromDirectoryAsync(Guid directoryId)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null)
            return ServiceResult<StallionDto>.Forbidden("Caller identity could not be resolved.");

        var farm = await _farmRepo.GetByUserIdAsync(caller.Id);
        if (farm == null)
            return ServiceResult<StallionDto>.NotFound("No stud farm found for the current user.");

        if (!farm.StudDirectoryId.HasValue)
            return ServiceResult<StallionDto>.Forbidden("Your farm is not linked to the stallion directory.");

        var entry = await _directoryRepo.GetByIdAsync(directoryId);
        if (entry == null)
            return ServiceResult<StallionDto>.NotFound("Stallion directory entry not found.");

        // Security: the directory entry must belong to this farm's linked stud.
        if (entry.StudDirectoryId != farm.StudDirectoryId.Value)
            return ServiceResult<StallionDto>.Forbidden(
                "This stallion does not belong to your stud's directory.");

        if (!entry.IsActive)
            return ServiceResult<StallionDto>.NotFound("Stallion directory entry not found.");

        // Prevent duplicate additions.
        var existingStallions = await _repo.GetByStudFarmIdAsync(farm.Id);
        var alreadyAdded = existingStallions.Any(s => s.StallionDirectoryId == directoryId);
        if (alreadyAdded)
            return ServiceResult<StallionDto>.Conflict("This stallion has already been added to your stable.");

        // Snapshot the directory data — decoupled from this point on.
        var stallion = new Stallion
        {
            StudFarmId = farm.Id,
            StallionDirectoryId = entry.Id,
            Name = entry.Name,
            YearOfBirth = entry.YearOfBirth,
            Colour = entry.Colour,
            Sire = entry.SireName,
            Dam = entry.DamName
        };

        var created = await _repo.AddAsync(stallion);
        return ServiceResult<StallionDto>.Created(MapToDto(created));
    }

    private static StallionSummaryDto MapToSummary(Stallion s) => new()
    {
        Id = s.Id,
        StudFarmId = s.StudFarmId,
        Name = s.Name,
        YearOfBirth = s.YearOfBirth,
        Colour = s.Colour,
        PrimaryImagePath = s.Images.FirstOrDefault(img => img.IsPrimary)?.BlobPath,
        ActiveListingCount = s.Listings.Count(l => l.Status == ListingStatus.Active),
        TotalListingCount = s.Listings.Count,
        IsActive = s.IsActive
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
        StallionDirectoryId = s.StallionDirectoryId,
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
