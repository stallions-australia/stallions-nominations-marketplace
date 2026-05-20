namespace Stallions.Shared.DTOs.Enquiries;

public class OpenEnquiryRequest
{
    public string? Subject { get; set; }
    public required string Body { get; set; }
}
