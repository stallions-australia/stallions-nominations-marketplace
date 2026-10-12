using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data;
using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Shared.DTOs.Terms;
using Stallions.Shared.Enums;

namespace Stallions.Server.Services;

public class TermsService : ITermsService
{
    public const string PublishAuditAction = "PublishTerms";
    public const string AcceptStudAuditAction = "AcceptStudTerms";
    private const string StaffOnlyMessage = "Only Stallions Australia staff can manage terms.";

    private readonly ITermsRepository _repo;
    private readonly IUserService _users;
    private readonly IStudFarmRepository _farms;
    private readonly IAuditLogRepository _audit;
    private readonly ITransactionRunner _transactions;
    private readonly TimeProvider _clock;

    public TermsService(ITermsRepository repo, IUserService users, IStudFarmRepository farms, IAuditLogRepository audit,
        ITransactionRunner transactions, TimeProvider clock)
    {
        _repo = repo;
        _users = users;
        _farms = farms;
        _audit = audit;
        _transactions = transactions;
        _clock = clock;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    public async Task<ServiceResult<TermsDocumentDto>> GetCurrentAsync()
    {
        var current = await _repo.GetCurrentAsync(TermsAudience.Buyer);
        return current == null
            ? ServiceResult<TermsDocumentDto>.NotFound("No Terms & Conditions have been published yet.")
            : ServiceResult<TermsDocumentDto>.Ok(MapToDto(current));
    }

    public async Task<ServiceResult<TermsDocumentDto>> PublishAsync(PublishTermsRequest request)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null || caller.Role != UserRole.Staff)
            return ServiceResult<TermsDocumentDto>.Forbidden(StaffOnlyMessage);
        if (!TryAudience(request.Audience, out var audience))
            return ServiceResult<TermsDocumentDto>.BadRequest("Choose Buyer or Stud terms.");
        if (string.IsNullOrWhiteSpace(request.Body))
            return ServiceResult<TermsDocumentDto>.BadRequest("Terms body cannot be empty.");

        try
        {
            // The new version and its audit row commit together: a publish never exists without its audit row.
            return await _transactions.RunAsync(async () =>
            {
                var current = await _repo.GetCurrentAsync(audience);
                var created = await _repo.AddAsync(new TermsDocument
                {
                    Audience = audience,
                    Version = (current?.Version ?? 0) + 1,
                    Body = request.Body.Trim(),
                    CreatedByUserId = caller.Id,
                    CreatedAt = Now
                });
                await _audit.LogAsync("TermsDocument", created.Id, PublishAuditAction, caller.Id, JsonSerializer.Serialize(new
                {
                    Audience = audience.ToString(),
                    created.Version,
                    PreviousVersion = current?.Version
                }));
                created.CreatedBy = caller;
                return ServiceResult<TermsDocumentDto>.Created(MapToDto(created));
            });
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            return ServiceResult<TermsDocumentDto>.Conflict("Another version was published at the same moment. Reload and try again.");
        }
    }

    public async Task<ServiceResult<IReadOnlyList<TermsDocumentDto>>> GetHistoryAsync(string? audience)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null || caller.Role != UserRole.Staff)
            return ServiceResult<IReadOnlyList<TermsDocumentDto>>.Forbidden(StaffOnlyMessage);
        if (!TryAudience(audience, out var parsed))
            return ServiceResult<IReadOnlyList<TermsDocumentDto>>.BadRequest("Choose Buyer or Stud terms.");

        var docs = await _repo.GetHistoryAsync(parsed);
        return ServiceResult<IReadOnlyList<TermsDocumentDto>>.Ok(docs.Select(MapToDto).ToList());
    }

    public async Task<ServiceResult<StudTermsStatusDto>> GetStudStatusAsync()
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null || caller.Role is not (UserRole.StudFarmAdmin or UserRole.Staff))
            return ServiceResult<StudTermsStatusDto>.Forbidden("Only stud farm admins and staff can see the stud listing terms.");

        var current = await _repo.GetCurrentAsync(TermsAudience.Stud);
        if (caller.Role == UserRole.Staff)
            return ServiceResult<StudTermsStatusDto>.Ok(Status(current, farm: null));

        var farm = await _farms.GetByUserIdAsync(caller.Id);
        if (farm == null) return ServiceResult<StudTermsStatusDto>.Forbidden("No stud farm found for the current user.");
        return ServiceResult<StudTermsStatusDto>.Ok(Status(current, farm));
    }

    public async Task<ServiceResult<StudTermsStatusDto>> AcceptStudTermsAsync(AcceptTermsRequest request)
    {
        var caller = await _users.GetOrCreateCurrentUserAsync();
        if (caller == null || caller.Role != UserRole.StudFarmAdmin)
            return ServiceResult<StudTermsStatusDto>.Forbidden("Only stud farm admins can accept the stud listing terms.");

        // The acceptance and its audit row commit together; the farm is read inside the unit of work
        // (the runner starts each attempt with a clean change tracker).
        return await _transactions.RunAsync(async () =>
        {
            var farm = await _farms.GetByUserIdAsync(caller.Id);
            if (farm == null) return ServiceResult<StudTermsStatusDto>.Forbidden("No stud farm found for the current user.");

            var current = await _repo.GetCurrentAsync(TermsAudience.Stud);
            if (current == null)
                return ServiceResult<StudTermsStatusDto>.BadRequest("No stud listing terms are currently published.");
            if (request.Version != current.Version)
                return ServiceResult<StudTermsStatusDto>.BadRequest("The stud listing terms have changed. Please review the latest version.");

            farm.AcceptedStudTermsVersion = current.Version;
            farm.AcceptedStudTermsAt = Now;
            farm.AcceptedStudTermsByUserId = caller.Id;
            await _farms.UpdateAsync(farm);
            await _audit.LogAsync("StudFarm", farm.Id, AcceptStudAuditAction, caller.Id,
                JsonSerializer.Serialize(new { current.Version, StudFarmName = farm.Name }));
            return ServiceResult<StudTermsStatusDto>.Ok(Status(current, farm));
        });
    }

    private static StudTermsStatusDto Status(TermsDocument? current, StudFarm? farm) => new()
    {
        Current = current == null ? null : MapToDto(current),
        AcceptedVersion = farm?.AcceptedStudTermsVersion,
        AcceptedAt = farm?.AcceptedStudTermsAt,
        NeedsAcceptance = farm != null && current != null && farm.AcceptedStudTermsVersion != current.Version
    };

    /// <summary>Empty = Buyer; otherwise exactly "Buyer" or "Stud", any case (no numbers or comma-joined names).</summary>
    private static bool TryAudience(string? value, out TermsAudience audience)
    {
        audience = TermsAudience.Buyer;
        if (string.IsNullOrWhiteSpace(value)) return true;
        var name = Enum.GetNames<TermsAudience>().FirstOrDefault(n => string.Equals(n, value.Trim(), StringComparison.OrdinalIgnoreCase));
        if (name == null) return false;
        audience = Enum.Parse<TermsAudience>(name);
        return true;
    }

    private static TermsDocumentDto MapToDto(TermsDocument t) => new()
    {
        Id = t.Id,
        Audience = t.Audience.ToString(),
        Version = t.Version,
        Body = t.Body,
        CreatedAt = t.CreatedAt,
        CreatedByName = t.CreatedBy?.DisplayName
    };
}
