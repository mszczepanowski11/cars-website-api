using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

/// <summary>
/// GetUserId()/IsAdmin() were independently copy-pasted (with drift - e.g. FindFirstValue vs
/// HasClaim) into most controllers; this centralizes the one canonical implementation.
/// </summary>
public abstract class CarizoControllerBase : ControllerBase
{
    /// <summary>0 for an anonymous/unauthenticated caller.</summary>
    protected int GetUserId()
    {
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId);
        return userId;
    }

    protected bool IsAdmin() => User.FindFirstValue("isAdmin") == "true";
}
