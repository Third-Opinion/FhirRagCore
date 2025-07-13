using System.Security.Claims;
using Microsoft.Extensions.Logging;
using FhirRag.Core.Models;

namespace FhirRag.Core.Security;

/// <summary>
/// Security context for tracking user and tenant information
/// </summary>
public class SecurityContext
{
    public string UserId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
    public List<string> Permissions { get; set; } = new();
    public Dictionary<string, string> Claims { get; set; } = new();
    public DateTime? AuthenticatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
    public bool IsAuthenticated => !string.IsNullOrEmpty(UserId);
    public bool IsSystemUser { get; set; } = false;

    /// <summary>
    /// Creates a security context from claims principal
    /// </summary>
    public static SecurityContext FromClaimsPrincipal(ClaimsPrincipal principal, string tenantId)
    {
        var context = new SecurityContext
        {
            TenantId = tenantId,
            UserId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty,
            UserName = principal.FindFirst(ClaimTypes.Name)?.Value ?? string.Empty,
            Email = principal.FindFirst(ClaimTypes.Email)?.Value ?? string.Empty
        };

        // Extract roles
        context.Roles = principal.FindAll(ClaimTypes.Role)
            .Select(c => c.Value)
            .ToList();

        // Extract custom permissions
        context.Permissions = principal.FindAll("permission")
            .Select(c => c.Value)
            .ToList();

        // Store all claims for reference
        context.Claims = principal.Claims
            .ToDictionary(c => c.Type, c => c.Value);

        return context;
    }

    /// <summary>
    /// Checks if user has a specific role
    /// </summary>
    public bool HasRole(string role)
    {
        return Roles.Contains(role, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks if user has a specific permission
    /// </summary>
    public bool HasPermission(string permission)
    {
        return Permissions.Contains(permission, StringComparer.OrdinalIgnoreCase) ||
               IsSystemUser;
    }

    /// <summary>
    /// Checks if user has any of the specified roles
    /// </summary>
    public bool HasAnyRole(params string[] roles)
    {
        return roles.Any(role => HasRole(role));
    }

    /// <summary>
    /// Checks if user has all of the specified permissions
    /// </summary>
    public bool HasAllPermissions(params string[] permissions)
    {
        return permissions.All(permission => HasPermission(permission));
    }

    /// <summary>
    /// Checks if the context has expired
    /// </summary>
    public bool IsExpired()
    {
        return ExpiresAt.HasValue && ExpiresAt.Value <= DateTime.UtcNow;
    }

    /// <summary>
    /// Gets a specific claim value
    /// </summary>
    public string? GetClaim(string claimType)
    {
        return Claims.TryGetValue(claimType, out var value) ? value : null;
    }

    /// <summary>
    /// Gets all permissions
    /// </summary>
    public IEnumerable<string> GetPermissions()
    {
        return Permissions;
    }

    /// <summary>
    /// Gets all roles
    /// </summary>
    public IEnumerable<string> GetRoles()
    {
        return Roles;
    }
}

/// <summary>
/// Tenant validation service
/// </summary>
public class TenantValidator
{
    private readonly ILogger<TenantValidator> _logger;
    private readonly HashSet<string> _validTenants;

    public TenantValidator(ILogger<TenantValidator> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _validTenants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Validates if a tenant ID is valid
    /// </summary>
    public bool IsValidTenant(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            _logger.LogWarning("Empty tenant ID provided for validation");
            return false;
        }

        // Check format
        if (!IsValidTenantFormat(tenantId))
        {
            _logger.LogWarning("Invalid tenant ID format: {TenantId}", tenantId);
            return false;
        }

        // For now, accept any properly formatted tenant ID
        // In production, this would check against a tenant registry
        _logger.LogDebug("Tenant ID validated: {TenantId}", tenantId);
        return true;
    }

    /// <summary>
    /// Validates tenant ID format
    /// </summary>
    private static bool IsValidTenantFormat(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            return false;

        if (tenantId.Length < 3 || tenantId.Length > 50)
            return false;

        // Allow alphanumeric, hyphens, and underscores
        return tenantId.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_');
    }

    /// <summary>
    /// Registers a valid tenant
    /// </summary>
    public void RegisterTenant(string tenantId)
    {
        if (IsValidTenantFormat(tenantId))
        {
            _validTenants.Add(tenantId);
            _logger.LogInformation("Registered tenant: {TenantId}", tenantId);
        }
        else
        {
            throw new ArgumentException($"Invalid tenant ID format: {tenantId}", nameof(tenantId));
        }
    }

    /// <summary>
    /// Removes a tenant from valid list
    /// </summary>
    public void UnregisterTenant(string tenantId)
    {
        if (_validTenants.Remove(tenantId))
        {
            _logger.LogInformation("Unregistered tenant: {TenantId}", tenantId);
        }
    }

    /// <summary>
    /// Gets all registered tenants
    /// </summary>
    public IEnumerable<string> GetRegisteredTenants()
    {
        return _validTenants.ToList();
    }
}

/// <summary>
/// Access control permissions
/// </summary>
public static class Permissions
{
    // FHIR resource permissions
    public const string ReadPatient = "fhir:patient:read";
    public const string WritePatient = "fhir:patient:write";
    public const string ReadObservation = "fhir:observation:read";
    public const string WriteObservation = "fhir:observation:write";
    public const string ReadCondition = "fhir:condition:read";
    public const string WriteCondition = "fhir:condition:write";
    public const string ReadMedication = "fhir:medication:read";
    public const string WriteMedication = "fhir:medication:write";
    public const string ReadProcedure = "fhir:procedure:read";
    public const string WriteProcedure = "fhir:procedure:write";

    // System permissions
    public const string SystemAdmin = "system:admin";
    public const string TenantAdmin = "tenant:admin";
    public const string ProcessingRead = "processing:read";
    public const string ProcessingWrite = "processing:write";
    public const string TelemetryRead = "telemetry:read";
    public const string TelemetryWrite = "telemetry:write";
    public const string FeedbackRead = "feedback:read";
    public const string FeedbackWrite = "feedback:write";

    // Query permissions
    public const string QueryExecute = "query:execute";
    public const string QueryHistory = "query:history";
    public const string QueryAdmin = "query:admin";

    /// <summary>
    /// Gets all FHIR read permissions
    /// </summary>
    public static readonly string[] AllFhirRead = {
        ReadPatient, ReadObservation, ReadCondition, ReadMedication, ReadProcedure
    };

    /// <summary>
    /// Gets all FHIR write permissions
    /// </summary>
    public static readonly string[] AllFhirWrite = {
        WritePatient, WriteObservation, WriteCondition, WriteMedication, WriteProcedure
    };

    /// <summary>
    /// Gets all system admin permissions
    /// </summary>
    public static string[] SystemAdminPermissions => new[] {
        SystemAdmin, TenantAdmin, ProcessingRead, ProcessingWrite,
        TelemetryRead, TelemetryWrite, FeedbackRead, FeedbackWrite,
        QueryExecute, QueryHistory, QueryAdmin
    }.Concat(AllFhirRead).Concat(AllFhirWrite).ToArray();
}

/// <summary>
/// Common roles in the system
/// </summary>
public static class Roles
{
    public const string SystemAdmin = "SystemAdmin";
    public const string TenantAdmin = "TenantAdmin";
    public const string Clinician = "Clinician";
    public const string Researcher = "Researcher";
    public const string DataAnalyst = "DataAnalyst";
    public const string ReadOnlyUser = "ReadOnlyUser";

    /// <summary>
    /// Gets default permissions for a role
    /// </summary>
    public static string[] GetDefaultPermissions(string role)
    {
        return role switch
        {
            SystemAdmin => Permissions.SystemAdminPermissions,
            TenantAdmin => new[] {
                Permissions.TenantAdmin, Permissions.ProcessingRead, Permissions.ProcessingWrite,
                Permissions.TelemetryRead, Permissions.FeedbackRead, Permissions.FeedbackWrite,
                Permissions.QueryExecute, Permissions.QueryHistory
            }.Concat(Permissions.AllFhirRead).Concat(Permissions.AllFhirWrite).ToArray(),
            Clinician => new[] {
                Permissions.QueryExecute, Permissions.QueryHistory, Permissions.FeedbackWrite
            }.Concat(Permissions.AllFhirRead).ToArray(),
            Researcher => new[] {
                Permissions.QueryExecute, Permissions.QueryHistory, Permissions.TelemetryRead
            }.Concat(Permissions.AllFhirRead).ToArray(),
            DataAnalyst => new[] {
                Permissions.QueryExecute, Permissions.QueryHistory, Permissions.TelemetryRead,
                Permissions.ProcessingRead
            }.Concat(Permissions.AllFhirRead).ToArray(),
            ReadOnlyUser => Permissions.AllFhirRead.Concat(new[] { Permissions.QueryExecute }).ToArray(),
            _ => Array.Empty<string>()
        };
    }
}
