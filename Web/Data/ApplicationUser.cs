using Microsoft.AspNetCore.Identity;

namespace RustPlusWeb.Data;

public sealed class ApplicationUser : IdentityUser
{
    public string DisplayName { get; set; } = "";
}
