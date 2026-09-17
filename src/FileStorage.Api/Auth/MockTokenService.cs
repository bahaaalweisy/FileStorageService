using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace FileStorage.Api.Auth;

public sealed record MockTokenResult(string AccessToken, DateTime ExpiresAtUtc, string UserId, string Role, string DisplayName);

public sealed class MockTokenService
{
    private readonly JwtOptions _options;

    public MockTokenService(IOptions<JwtOptions> options) => _options = options.Value;

    public MockTokenResult IssueFor(DemoIdentity identity)
    {
        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(_options.AccessTokenMinutes);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, identity.UserId),
            new Claim("sub", identity.UserId),
            new Claim(ClaimTypes.Role, identity.Role),
            new Claim("name", identity.DisplayName),
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: credentials);

        var handler = new JwtSecurityTokenHandler();
        var encoded = handler.WriteToken(token);

        return new MockTokenResult(encoded, expires, identity.UserId, identity.Role, identity.DisplayName);
    }
}
