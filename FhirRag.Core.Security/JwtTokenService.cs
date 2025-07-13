using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace FhirRag.Core.Security;

/// <summary>
/// Configuration for JWT token validation
/// </summary>
public class JwtConfiguration
{
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromHours(8);
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(30);
    public bool ValidateIssuer { get; set; } = true;
    public bool ValidateAudience { get; set; } = true;
    public bool ValidateLifetime { get; set; } = true;
    public bool ValidateIssuerSigningKey { get; set; } = true;
    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>
/// Service for JWT token creation and validation
/// </summary>
public class JwtTokenService
{
    private readonly JwtConfiguration _configuration;
    private readonly ILogger<JwtTokenService> _logger;
    private readonly JwtSecurityTokenHandler _tokenHandler;
    private readonly TokenValidationParameters _validationParameters;

    public JwtTokenService(JwtConfiguration configuration, ILogger<JwtTokenService> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tokenHandler = new JwtSecurityTokenHandler();
        
        ValidateConfiguration();
        _validationParameters = CreateValidationParameters();
    }

    /// <summary>
    /// Creates a JWT token for a user
    /// </summary>
    public string CreateToken(SecurityContext context, Dictionary<string, object>? additionalClaims = null)
    {
        try
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, context.UserId),
                new(ClaimTypes.Name, context.UserName),
                new(ClaimTypes.Email, context.Email),
                new("tenant_id", context.TenantId),
                new("auth_time", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString())
            };

            // Add roles
            claims.AddRange(context.Roles.Select(role => new Claim(ClaimTypes.Role, role)));

            // Add permissions
            claims.AddRange(context.Permissions.Select(permission => new Claim("permission", permission)));

            // Add additional claims
            if (additionalClaims != null)
            {
                foreach (var claim in additionalClaims)
                {
                    claims.Add(new Claim(claim.Key, claim.Value.ToString() ?? string.Empty));
                }
            }

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration.SecretKey));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _configuration.Issuer,
                audience: _configuration.Audience,
                claims: claims,
                expires: DateTime.UtcNow.Add(_configuration.TokenLifetime),
                signingCredentials: credentials
            );

            var tokenString = _tokenHandler.WriteToken(token);
            
            _logger.LogDebug("Created JWT token for user {UserId} with expiration {Expiration}", 
                context.UserId, token.ValidTo);

            return tokenString;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating JWT token for user {UserId}", context.UserId);
            throw;
        }
    }

    /// <summary>
    /// Validates a JWT token and returns the claims principal
    /// </summary>
    public async Task<TokenValidationResult> ValidateTokenAsync(string token)
    {
        try
        {
            _logger.LogDebug("Validating JWT token");

            if (string.IsNullOrWhiteSpace(token))
            {
                return TokenValidationResult.Failed("Token is null or empty");
            }

            var result = await _tokenHandler.ValidateTokenAsync(token, _validationParameters);
            
            if (!result.IsValid)
            {
                var errorMessage = result.Exception?.Message ?? "Token validation failed";
                _logger.LogWarning("JWT token validation failed: {Error}", errorMessage);
                return TokenValidationResult.Failed(errorMessage);
            }

            var principal = new ClaimsPrincipal(result.ClaimsIdentity);
            
            _logger.LogDebug("JWT token validation successful for user {UserId}", 
                principal.FindFirst(ClaimTypes.NameIdentifier)?.Value);

            return TokenValidationResult.Success(principal);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating JWT token");
            return TokenValidationResult.Failed($"Token validation error: {ex.Message}");
        }
    }

    /// <summary>
    /// Extracts claims from a token without validation (for debugging)
    /// </summary>
    public ClaimsPrincipal? ExtractClaimsWithoutValidation(string token)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(token))
                return null;

            var jsonToken = _tokenHandler.ReadJwtToken(token);
            return new ClaimsPrincipal(new ClaimsIdentity(jsonToken.Claims));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error extracting claims from token");
            return null;
        }
    }

    /// <summary>
    /// Checks if a token is expired
    /// </summary>
    public bool IsTokenExpired(string token)
    {
        try
        {
            var jsonToken = _tokenHandler.ReadJwtToken(token);
            return jsonToken.ValidTo < DateTime.UtcNow;
        }
        catch
        {
            return true; // Consider invalid tokens as expired
        }
    }

    /// <summary>
    /// Gets token expiration time
    /// </summary>
    public DateTime? GetTokenExpiration(string token)
    {
        try
        {
            var jsonToken = _tokenHandler.ReadJwtToken(token);
            return jsonToken.ValidTo;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Creates a refresh token
    /// </summary>
    public string CreateRefreshToken()
    {
        var randomBytes = new byte[32];
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        return Convert.ToBase64String(randomBytes);
    }

    private void ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_configuration.Issuer))
            throw new ArgumentException("JWT Issuer cannot be null or empty");

        if (string.IsNullOrWhiteSpace(_configuration.Audience))
            throw new ArgumentException("JWT Audience cannot be null or empty");

        if (string.IsNullOrWhiteSpace(_configuration.SecretKey))
            throw new ArgumentException("JWT SecretKey cannot be null or empty");

        if (_configuration.SecretKey.Length < 32)
            throw new ArgumentException("JWT SecretKey must be at least 32 characters long");
    }

    private TokenValidationParameters CreateValidationParameters()
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration.SecretKey));

        return new TokenValidationParameters
        {
            ValidateIssuer = _configuration.ValidateIssuer,
            ValidateAudience = _configuration.ValidateAudience,
            ValidateLifetime = _configuration.ValidateLifetime,
            ValidateIssuerSigningKey = _configuration.ValidateIssuerSigningKey,
            ValidIssuer = _configuration.Issuer,
            ValidAudience = _configuration.Audience,
            IssuerSigningKey = key,
            ClockSkew = _configuration.ClockSkew
        };
    }
}

/// <summary>
/// Result of token validation
/// </summary>
public class TokenValidationResult
{
    public bool IsValid { get; init; }
    public ClaimsPrincipal? Principal { get; init; }
    public string? ErrorMessage { get; init; }

    private TokenValidationResult() { }

    public static TokenValidationResult Success(ClaimsPrincipal principal)
    {
        return new TokenValidationResult
        {
            IsValid = true,
            Principal = principal
        };
    }

    public static TokenValidationResult Failed(string errorMessage)
    {
        return new TokenValidationResult
        {
            IsValid = false,
            ErrorMessage = errorMessage
        };
    }
}

/// <summary>
/// Extensions for JWT token service
/// </summary>
public static class JwtTokenServiceExtensions
{
    /// <summary>
    /// Creates a security context from a validated token
    /// </summary>
    public static SecurityContext CreateSecurityContext(this TokenValidationResult result, string tenantId)
    {
        if (!result.IsValid || result.Principal == null)
            throw new InvalidOperationException("Cannot create security context from invalid token result");

        return SecurityContext.FromClaimsPrincipal(result.Principal, tenantId);
    }

    /// <summary>
    /// Extracts tenant ID from token claims
    /// </summary>
    public static string? ExtractTenantId(this ClaimsPrincipal principal)
    {
        return principal.FindFirst("tenant_id")?.Value;
    }

    /// <summary>
    /// Checks if token contains a specific permission
    /// </summary>
    public static bool HasTokenPermission(this ClaimsPrincipal principal, string permission)
    {
        return principal.FindAll("permission").Any(c => c.Value.Equals(permission, StringComparison.OrdinalIgnoreCase));
    }
}