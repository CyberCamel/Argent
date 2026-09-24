using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using Argent.Core.Identity;

namespace Argent.Web.Factories;

public class AdditionalUserClaimsPrincipalFactory : UserClaimsPrincipalFactory<InternalUser, IdentityRole<Guid>>
{
    public AdditionalUserClaimsPrincipalFactory(
        UserManager<InternalUser> userManager,
        RoleManager<IdentityRole<Guid>> roleManager,
        IOptions<IdentityOptions> optionsAccessor)
        : base(userManager, roleManager, optionsAccessor)
    { }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(InternalUser user)
    {
        // 1. Let the base identity be created (Username, UserID, etc.)
        var identity = await base.GenerateClaimsAsync(user);

        // 2. Add our custom "FirstName" claim from the database object
        identity.AddClaim(new Claim("FirstName", user.FirstName ?? string.Empty));

        // 3. You can even add the LastName or a FullName here if you want
        identity.AddClaim(new Claim("LastName", user.LastName ?? string.Empty));

        return identity;
    }
}