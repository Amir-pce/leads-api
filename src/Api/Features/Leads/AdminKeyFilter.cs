using System.Security.Cryptography;
using System.Text;

namespace Api.Features.Leads;

/// <summary>
/// Guards the admin endpoints with a shared key sent as <c>X-Admin-Key</c>.
///
/// Deliberately simple: this API has exactly one operator. A full identity stack would be
/// more code to maintain for no gain here. If a second person ever needs access, replace
/// this with proper authentication rather than handing out a second copy of the key.
///
/// The key is read from configuration and must never be committed. Set it with:
///     dotnet user-secrets set "Admin:Key" "&lt;a long random string&gt;"
/// and in production supply it as the environment variable Admin__Key.
/// </summary>
public class AdminKeyFilter(IConfiguration config, ILogger<AdminKeyFilter> log) : IEndpointFilter
{
    private const string HeaderName = "X-Admin-Key";

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var expected = config["Admin:Key"];

        if (string.IsNullOrWhiteSpace(expected))
        {
            // Failing closed: an unset key locks the admin API rather than opening it.
            log.LogError("Admin:Key is not configured; refusing all admin requests.");
            return Results.Problem(
                title: "Admin API unavailable",
                detail: "The admin key is not configured on this server.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var provided = context.HttpContext.Request.Headers[HeaderName].ToString();

        if (!FixedTimeEquals(provided, expected))
        {
            return Results.Problem(
                title: "Unauthorized",
                detail: $"Send a valid {HeaderName} header.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        return await next(context);
    }

    /// <summary>
    /// Compares in constant time. A plain string comparison returns as soon as two bytes
    /// differ, which leaks how much of the key was correct.
    /// </summary>
    private static bool FixedTimeEquals(string a, string b)
    {
        if (string.IsNullOrEmpty(a)) return false;

        var left = Encoding.UTF8.GetBytes(a);
        var right = Encoding.UTF8.GetBytes(b);

        return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
    }
}
