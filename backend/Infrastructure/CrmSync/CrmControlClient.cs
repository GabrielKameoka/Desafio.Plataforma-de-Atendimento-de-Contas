using System.Net.Http.Json;
using Application.CrmSync;

namespace Infrastructure.CrmSync;

public sealed class CrmControlClient(HttpClient http) : ICrmControlGateway
{
    private sealed record Availability(bool Available);

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        var state = await http.GetFromJsonAsync<Availability>("control/state", cancellationToken);
        return state?.Available ?? throw new HttpRequestException("Estado do CRM indisponível.");
    }

    public async Task<bool> SetAvailableAsync(bool available, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsync(available ? "control/available" : "control/unavailable",
            null, cancellationToken);
        response.EnsureSuccessStatusCode();
        return available;
    }
}
