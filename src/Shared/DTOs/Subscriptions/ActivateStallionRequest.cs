namespace Stallions.Shared.DTOs.Subscriptions;

// StudFarmAdmin: activate one of your stallions for the open season by paying the listing fee.
public class ActivateStallionRequest
{
    public Guid StallionId { get; set; }
}
