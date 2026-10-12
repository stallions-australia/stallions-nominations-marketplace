using Stallions.Server.Data.Entities;
using Stallions.Server.Data.Repositories;
using Stallions.Shared.Enums;

namespace Stallions.Server.Services;

/// <summary>
/// A stud must accept the current stud listing terms before it pays a listing fee (Activate), publishes a listing or
/// converts an unsold lot to Make an Offer. No stud terms published = no gate. Staff actions never use this gate.
/// </summary>
public sealed class StudTermsGate(ITermsRepository terms)
{
    public const string NotAcceptedMessage = "Accept the current stud listing terms first.";

    public async Task<bool> IsSatisfiedAsync(StudFarm farm)
    {
        var current = await terms.GetCurrentAsync(TermsAudience.Stud);
        return current == null || farm.AcceptedStudTermsVersion == current.Version;
    }
}
