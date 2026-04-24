namespace porganizer.Api.Features.Auth;

public class AuthOptions
{
    public const string SectionName = "Auth";

    public bool Enabled { get; set; } = false;

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;
}
