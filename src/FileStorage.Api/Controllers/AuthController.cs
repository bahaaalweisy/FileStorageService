using FileStorage.Api.Auth;
using FileStorage.Api.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FileStorage.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly MockTokenService _tokenService;
    private readonly JwtOptions _options;

    public AuthController(MockTokenService tokenService, IOptions<JwtOptions> options)
    {
        _tokenService = tokenService;
        _options = options.Value;
    }

    [HttpGet("demo-identities")]
    [ProducesResponseType(typeof(IReadOnlyList<DemoIdentityResponse>), StatusCodes.Status200OK)]
    public IActionResult GetDemoIdentities()
    {
        if (!_options.EnableMockTokenEndpoint)
        {
            return NotFound();
        }

        var identities = DemoIdentities.All
            .Select(x => new DemoIdentityResponse(x.UserId, x.DisplayName, x.Role))
            .ToList();

        return Ok(identities);
    }

    [HttpPost("mock-token")]
    [ProducesResponseType(typeof(MockTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult IssueMockToken([FromBody] MockTokenRequest request)
    {
        if (!_options.EnableMockTokenEndpoint)
        {
            return NotFound();
        }

        var identity = DemoIdentities.FindByRole(request.Role);
        if (identity is null)
        {
            return Problem(
                title: "Validation failed",
                statusCode: StatusCodes.Status400BadRequest,
                detail: "Role must be 'user' or 'admin'.");
        }

        var result = _tokenService.IssueFor(identity);
        return Ok(new MockTokenResponse(result.AccessToken, result.ExpiresAtUtc, result.UserId, result.Role, result.DisplayName));
    }
}
