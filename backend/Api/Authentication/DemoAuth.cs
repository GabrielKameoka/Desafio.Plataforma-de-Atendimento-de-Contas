using System.Security.Claims;
using Application.Demo;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Api.Authentication;

public static class DemoAuth
{
    public const string CookieScheme = "DemoCookie";
    public const string StaffPolicy = "DemoStaff";
    public const string SupervisorPolicy = "DemoSupervisor";

    public static async Task SignInAsync(HttpContext context, SelectedDemoUser user)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.DisplayName),
            new Claim(ClaimTypes.Role, user.Role)
        ], CookieScheme);
        await context.SignInAsync(CookieScheme, new ClaimsPrincipal(identity));
    }

    public static SessionResponse? CurrentSession(ClaimsPrincipal principal)
    {
        var rawId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(rawId, out var id)
            ? new SessionResponse(id, principal.Identity?.Name ?? string.Empty,
                principal.FindFirstValue(ClaimTypes.Role) ?? string.Empty)
            : null;
    }
}

public sealed record SessionResponse(Guid Id, string DisplayName, string Role);

public sealed record SessionEndedResponse(bool SignedOut);
