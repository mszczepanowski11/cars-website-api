using System.Text;
using Hangfire.Dashboard;

/// <summary>
/// The Hangfire dashboard is a browser-navigated page, not an API endpoint - this app's normal
/// JWT-bearer auth has no way to attach itself to a plain browser navigation, so the dashboard
/// gets its own HTTP Basic Auth gate instead. Fails closed: if HANGFIRE_DASHBOARD_USER/PASSWORD
/// aren't set, every request is rejected rather than leaving the dashboard open to anyone.
/// </summary>
public class HangfireDashboardAuthFilter : IDashboardAuthorizationFilter
{
    private readonly string? _user;
    private readonly string? _password;

    public HangfireDashboardAuthFilter(IConfiguration config)
    {
        _user = Environment.GetEnvironmentVariable("HANGFIRE_DASHBOARD_USER") ?? config["Hangfire:DashboardUser"];
        _password = Environment.GetEnvironmentVariable("HANGFIRE_DASHBOARD_PASSWORD") ?? config["Hangfire:DashboardPassword"];
    }

    public bool Authorize(DashboardContext context)
    {
        if (string.IsNullOrEmpty(_user) || string.IsNullOrEmpty(_password)) return false;

        var httpContext = context.GetHttpContext();
        var header = httpContext.Request.Headers["Authorization"].ToString();
        if (string.IsNullOrEmpty(header) || !header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            httpContext.Response.Headers["WWW-Authenticate"] = "Basic realm=\"Hangfire\"";
            httpContext.Response.StatusCode = 401;
            return false;
        }

        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header["Basic ".Length..]));
            var parts = decoded.Split(':', 2);
            if (parts.Length == 2 && parts[0] == _user && parts[1] == _password) return true;
        }
        catch (FormatException)
        {
            // Malformed base64 in the Authorization header - fall through to the same 401 as any
            // other rejected credential instead of letting the exception bubble up as a 500.
        }

        httpContext.Response.Headers["WWW-Authenticate"] = "Basic realm=\"Hangfire\"";
        httpContext.Response.StatusCode = 401;
        return false;
    }
}
