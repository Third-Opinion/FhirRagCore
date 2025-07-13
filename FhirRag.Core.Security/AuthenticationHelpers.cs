using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace FhirRag.Core.Security;

/// <summary>
/// Helper methods for authentication and security operations
/// </summary>
public static class AuthenticationHelpers
{
    /// <summary>
    /// Hashes a password using SHA256 with salt
    /// </summary>
    public static string HashPassword(string password, string salt)
    {
        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("Password cannot be null or empty", nameof(password));

        if (string.IsNullOrWhiteSpace(salt))
            throw new ArgumentException("Salt cannot be null or empty", nameof(salt));

        using var sha256 = SHA256.Create();
        var saltedPassword = $"{password}{salt}";
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(saltedPassword));
        return Convert.ToBase64String(hashBytes);
    }

    /// <summary>
    /// Generates a cryptographically secure random salt
    /// </summary>
    public static string GenerateSalt(int length = 32)
    {
        if (length <= 0)
            throw new ArgumentException("Salt length must be positive", nameof(length));

        var saltBytes = new byte[length];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(saltBytes);
        return Convert.ToBase64String(saltBytes);
    }

    /// <summary>
    /// Verifies a password against a hash
    /// </summary>
    public static bool VerifyPassword(string password, string hash, string salt)
    {
        if (string.IsNullOrWhiteSpace(password))
            return false;

        try
        {
            var computedHash = HashPassword(password, salt);
            return string.Equals(hash, computedHash, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Generates a secure API key
    /// </summary>
    public static string GenerateApiKey(int length = 64)
    {
        if (length <= 0)
            throw new ArgumentException("API key length must be positive", nameof(length));

        var keyBytes = new byte[length];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(keyBytes);
        return Convert.ToBase64String(keyBytes);
    }

    /// <summary>
    /// Creates a secure session ID
    /// </summary>
    public static string GenerateSessionId()
    {
        return Guid.NewGuid().ToString("N");
    }

    /// <summary>
    /// Validates password strength
    /// </summary>
    public static PasswordValidationResult ValidatePasswordStrength(string password)
    {
        var result = new PasswordValidationResult();

        if (string.IsNullOrWhiteSpace(password))
        {
            result.AddError("Password cannot be empty");
            return result;
        }

        if (password.Length < 8)
            result.AddError("Password must be at least 8 characters long");

        if (password.Length > 128)
            result.AddError("Password cannot exceed 128 characters");

        if (!password.Any(char.IsUpper))
            result.AddError("Password must contain at least one uppercase letter");

        if (!password.Any(char.IsLower))
            result.AddError("Password must contain at least one lowercase letter");

        if (!password.Any(char.IsDigit))
            result.AddError("Password must contain at least one digit");

        if (!password.Any(c => !char.IsLetterOrDigit(c)))
            result.AddError("Password must contain at least one special character");

        // Check for common weak patterns
        if (ContainsCommonPatterns(password))
            result.AddError("Password contains common weak patterns");

        return result;
    }

    /// <summary>
    /// Sanitizes user input to prevent injection attacks
    /// </summary>
    public static string SanitizeInput(string input, int maxLength = 255)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        // Remove null characters and control characters
        var sanitized = new string(input.Where(c => c != '\0' && !char.IsControl(c) || char.IsWhiteSpace(c)).ToArray());

        // Trim and limit length
        sanitized = sanitized.Trim();
        if (sanitized.Length > maxLength)
        {
            sanitized = sanitized.Substring(0, maxLength);
        }

        return sanitized;
    }

    /// <summary>
    /// Validates an email address format
    /// </summary>
    public static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;

        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Creates a time-limited access code
    /// </summary>
    public static string CreateAccessCode(string identifier, TimeSpan validFor)
    {
        var timestamp = DateTimeOffset.UtcNow.Add(validFor).ToUnixTimeSeconds();
        var data = $"{identifier}:{timestamp}";
        var hash = ComputeHash(data);
        return $"{timestamp}:{hash}";
    }

    /// <summary>
    /// Validates a time-limited access code
    /// </summary>
    public static bool ValidateAccessCode(string code, string identifier)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(identifier))
            return false;

        try
        {
            var parts = code.Split(':');
            if (parts.Length != 2)
                return false;

            if (!long.TryParse(parts[0], out var timestamp))
                return false;

            var expiryTime = DateTimeOffset.FromUnixTimeSeconds(timestamp);
            if (expiryTime < DateTimeOffset.UtcNow)
                return false;

            var expectedData = $"{identifier}:{timestamp}";
            var expectedHash = ComputeHash(expectedData);

            return string.Equals(parts[1], expectedHash, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static bool ContainsCommonPatterns(string password)
    {
        var commonPatterns = new[]
        {
            "123456", "password", "admin", "user", "test",
            "qwerty", "abc123", "letmein", "welcome",
            "monkey", "dragon", "princess", "hello"
        };

        return commonPatterns.Any(pattern =>
            password.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }

    private static string ComputeHash(string input)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToBase64String(hashBytes);
    }
}

/// <summary>
/// Result of password validation
/// </summary>
public class PasswordValidationResult
{
    private readonly List<string> _errors = new();

    public bool IsValid => !_errors.Any();
    public IReadOnlyList<string> Errors => _errors.AsReadOnly();

    public void AddError(string error)
    {
        if (!string.IsNullOrWhiteSpace(error))
        {
            _errors.Add(error);
        }
    }
}

/// <summary>
/// Security context provider for request-scoped security information
/// </summary>
public class SecurityContextProvider
{
    private readonly ILogger<SecurityContextProvider> _logger;
    private SecurityContext? _currentContext;

    public SecurityContextProvider(ILogger<SecurityContextProvider> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets the current security context
    /// </summary>
    public SecurityContext? Current => _currentContext;

    /// <summary>
    /// Sets the current security context
    /// </summary>
    public void SetContext(SecurityContext context)
    {
        _currentContext = context ?? throw new ArgumentNullException(nameof(context));
        _logger.LogDebug("Security context set for user {UserId} in tenant {TenantId}",
            context.UserId, context.TenantId);
    }

    /// <summary>
    /// Clears the current security context
    /// </summary>
    public void ClearContext()
    {
        if (_currentContext != null)
        {
            _logger.LogDebug("Security context cleared for user {UserId}",
                _currentContext.UserId);
            _currentContext = null;
        }
    }

    /// <summary>
    /// Creates a system context for background operations
    /// </summary>
    public SecurityContext CreateSystemContext(string tenantId)
    {
        return new SecurityContext
        {
            UserId = "system",
            TenantId = tenantId,
            UserName = "System",
            Email = "system@fhirrag.internal",
            IsSystemUser = true,
            Roles = new List<string> { Roles.SystemAdmin },
            Permissions = Permissions.SystemAdminPermissions.ToList(),
            AuthenticatedAt = DateTime.UtcNow
        };
    }
}

/// <summary>
/// Audit log entry for security events
/// </summary>
public class SecurityAuditEntry
{
    public string EventId { get; set; } = Guid.NewGuid().ToString();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string EventType { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty;
    public string ResourceId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? FailureReason { get; set; }
    public Dictionary<string, object> AdditionalData { get; set; } = new();
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
}

/// <summary>
/// Security audit logger
/// </summary>
public class SecurityAuditLogger
{
    private readonly ILogger<SecurityAuditLogger> _logger;

    public SecurityAuditLogger(ILogger<SecurityAuditLogger> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Logs a security audit entry
    /// </summary>
    public void LogSecurityEvent(SecurityAuditEntry entry)
    {
        if (entry == null)
            throw new ArgumentNullException(nameof(entry));

        try
        {
            var logMessage = "Security Event: {EventType} by {UserId} in {TenantId} - {Success}";
            var logLevel = entry.Success ? LogLevel.Information : LogLevel.Warning;

            _logger.Log(logLevel, logMessage,
                entry.EventType, entry.UserId, entry.TenantId, entry.Success ? "Success" : "Failed");

            // In production, this would also write to a secure audit log store
            // that is tamper-proof and meets compliance requirements
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log security audit entry");
        }
    }

    /// <summary>
    /// Logs an authentication event
    /// </summary>
    public void LogAuthenticationEvent(string userId, string tenantId, bool success, string? failureReason = null)
    {
        var entry = new SecurityAuditEntry
        {
            EventType = "Authentication",
            UserId = userId,
            TenantId = tenantId,
            Action = "Login",
            Success = success,
            FailureReason = failureReason
        };

        LogSecurityEvent(entry);
    }

    /// <summary>
    /// Logs an authorization event
    /// </summary>
    public void LogAuthorizationEvent(string userId, string tenantId, string resourceType, string resourceId,
        string action, bool success, string? failureReason = null)
    {
        var entry = new SecurityAuditEntry
        {
            EventType = "Authorization",
            UserId = userId,
            TenantId = tenantId,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Action = action,
            Success = success,
            FailureReason = failureReason
        };

        LogSecurityEvent(entry);
    }
}