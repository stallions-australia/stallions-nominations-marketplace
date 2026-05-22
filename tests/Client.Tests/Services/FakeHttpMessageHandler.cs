using System.Net;
using System.Net.Http.Json;

namespace Stallions.Client.Tests.Services;

/// <summary>Minimal fake HTTP handler for testing typed API services.</summary>
public class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _status;
    private readonly object _body;

    public FakeHttpMessageHandler(HttpStatusCode status, object body)
    {
        _status = status;
        _body = body;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var response = new HttpResponseMessage(_status)
        {
            Content = JsonContent.Create(_body)
        };
        return Task.FromResult(response);
    }
}
