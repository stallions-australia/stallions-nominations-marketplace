namespace Stallions.Shared.DTOs.Enquiries;

public class CreateEnquiryRequest
{
    public string? Subject { get; set; }
    public required string Body { get; set; }
}
