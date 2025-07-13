using Microsoft.Extensions.Logging;
using FhirRag.Core.Models;
using FhirRag.Core.Abstractions;

namespace FhirRag.Core.Security;

/// <summary>
/// Service for managing access control and authorization
/// </summary>
public class AccessControlService
{
    private readonly ILogger<AccessControlService> _logger;
    private readonly TenantValidator _tenantValidator;

    public AccessControlService(
        ILogger<AccessControlService> logger,
        TenantValidator tenantValidator)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tenantValidator = tenantValidator ?? throw new ArgumentNullException(nameof(tenantValidator));
    }

    /// <summary>
    /// Validates if a user can access a specific FHIR resource
    /// </summary>
    public async Task<AccessResult> ValidateResourceAccessAsync(
        SecurityContext context,
        string resourceType,
        string resourceId,
        string operation,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Validating access for user {UserId} to {ResourceType} {ResourceId} for {Operation}",
                context.UserId, resourceType, resourceId, operation);

            // Validate tenant
            if (!_tenantValidator.IsValidTenant(context.TenantId))
            {
                return AccessResult.Denied("Invalid tenant");
            }

            // Check if user is authenticated
            if (!context.IsAuthenticated)
            {
                return AccessResult.Denied("User not authenticated");
            }

            // Build required permission
            var permission = $"fhir:{resourceType.ToLowerInvariant()}:{operation.ToLowerInvariant()}";

            // Check permission
            if (!context.HasPermission(permission))
            {
                _logger.LogWarning("User {UserId} denied access to {ResourceType} {ResourceId} - missing permission {Permission}",
                    context.UserId, resourceType, resourceId, permission);
                return AccessResult.Denied($"Missing permission: {permission}");
            }

            // Additional resource-specific checks
            var resourceAccessResult = await ValidateResourceSpecificAccessAsync(
                context, resourceType, resourceId, operation, cancellationToken);

            if (!resourceAccessResult.IsAllowed)
            {
                return resourceAccessResult;
            }

            _logger.LogDebug("Access granted for user {UserId} to {ResourceType} {ResourceId}",
                context.UserId, resourceType, resourceId);

            return AccessResult.Allowed();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating access for user {UserId} to {ResourceType} {ResourceId}",
                context.UserId, resourceType, resourceId);
            return AccessResult.Denied("Access validation failed");
        }
    }

    /// <summary>
    /// Validates if a user can execute a query
    /// </summary>
    public AccessResult ValidateQueryAccess(SecurityContext context, string queryType = "standard")
    {
        try
        {
            _logger.LogDebug("Validating query access for user {UserId} with query type {QueryType}",
                context.UserId, queryType);

            // Validate tenant
            if (!_tenantValidator.IsValidTenant(context.TenantId))
            {
                return AccessResult.Denied("Invalid tenant");
            }

            // Check if user is authenticated
            if (!context.IsAuthenticated)
            {
                return AccessResult.Denied("User not authenticated");
            }

            // Check query permission
            if (!context.HasPermission(Permissions.QueryExecute))
            {
                _logger.LogWarning("User {UserId} denied query access - missing permission {Permission}",
                    context.UserId, Permissions.QueryExecute);
                return AccessResult.Denied($"Missing permission: {Permissions.QueryExecute}");
            }

            // Additional checks for admin queries
            if (queryType.Equals("admin", StringComparison.OrdinalIgnoreCase))
            {
                if (!context.HasPermission(Permissions.QueryAdmin))
                {
                    return AccessResult.Denied($"Missing permission: {Permissions.QueryAdmin}");
                }
            }

            _logger.LogDebug("Query access granted for user {UserId}", context.UserId);
            return AccessResult.Allowed();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating query access for user {UserId}", context.UserId);
            return AccessResult.Denied("Query access validation failed");
        }
    }

    /// <summary>
    /// Validates if a user can access telemetry data
    /// </summary>
    public AccessResult ValidateTelemetryAccess(SecurityContext context, string operation = "read")
    {
        try
        {
            _logger.LogDebug("Validating telemetry access for user {UserId} with operation {Operation}",
                context.UserId, operation);

            // Validate tenant
            if (!_tenantValidator.IsValidTenant(context.TenantId))
            {
                return AccessResult.Denied("Invalid tenant");
            }

            // Check if user is authenticated
            if (!context.IsAuthenticated)
            {
                return AccessResult.Denied("User not authenticated");
            }

            // Check telemetry permission
            var permission = operation.ToLowerInvariant() switch
            {
                "read" => Permissions.TelemetryRead,
                "write" => Permissions.TelemetryWrite,
                _ => throw new ArgumentException($"Unknown telemetry operation: {operation}")
            };

            if (!context.HasPermission(permission))
            {
                _logger.LogWarning("User {UserId} denied telemetry access - missing permission {Permission}",
                    context.UserId, permission);
                return AccessResult.Denied($"Missing permission: {permission}");
            }

            _logger.LogDebug("Telemetry access granted for user {UserId}", context.UserId);
            return AccessResult.Allowed();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating telemetry access for user {UserId}", context.UserId);
            return AccessResult.Denied("Telemetry access validation failed");
        }
    }

    /// <summary>
    /// Validates tenant isolation for data access
    /// </summary>
    public bool ValidateTenantIsolation(SecurityContext context, string dataTenantId)
    {
        if (string.IsNullOrWhiteSpace(dataTenantId))
        {
            _logger.LogWarning("Empty data tenant ID provided for isolation validation");
            return false;
        }

        // System users can access any tenant
        if (context.IsSystemUser || context.HasPermission(Permissions.SystemAdmin))
        {
            return true;
        }

        // Regular users can only access their own tenant data
        var canAccess = string.Equals(context.TenantId, dataTenantId, StringComparison.OrdinalIgnoreCase);

        if (!canAccess)
        {
            _logger.LogWarning("Tenant isolation violation: User {UserId} from tenant {UserTenant} attempted to access data from tenant {DataTenant}",
                context.UserId, context.TenantId, dataTenantId);
        }

        return canAccess;
    }

    /// <summary>
    /// Creates a filter for tenant-isolated data queries
    /// </summary>
    public string CreateTenantFilter(SecurityContext context)
    {
        // System users can see all data
        if (context.IsSystemUser || context.HasPermission(Permissions.SystemAdmin))
        {
            return string.Empty; // No filter needed
        }

        // Regular users only see their tenant data
        return $"tenant_id = '{context.TenantId}'";
    }

    /// <summary>
    /// Validates resource-specific access rules
    /// </summary>
    private async Task<AccessResult> ValidateResourceSpecificAccessAsync(
        SecurityContext context,
        string resourceType,
        string resourceId,
        string operation,
        CancellationToken cancellationToken)
    {
        // For now, implement basic checks
        // In production, this would include more sophisticated rules:
        // - Patient-specific access controls
        // - Care team membership
        // - Consent management
        // - Data sharing agreements

        await Task.CompletedTask; // Placeholder for async operations

        // Example: Patients can only read their own data
        if (resourceType.Equals("Patient", StringComparison.OrdinalIgnoreCase) &&
            context.HasRole(Roles.ReadOnlyUser) &&
            !context.HasAnyRole(Roles.Clinician, Roles.Researcher))
        {
            // Would need to check if resourceId matches user's patient ID
            // This is a simplified example
        }

        return AccessResult.Allowed();
    }
}

/// <summary>
/// Result of an access control check
/// </summary>
public class AccessResult
{
    public bool IsAllowed { get; init; }
    public string? DenialReason { get; init; }
    public Dictionary<string, object> Context { get; init; } = new();

    private AccessResult() { }

    public static AccessResult Allowed(Dictionary<string, object>? context = null)
    {
        return new AccessResult
        {
            IsAllowed = true,
            Context = context ?? new Dictionary<string, object>()
        };
    }

    public static AccessResult Denied(string reason, Dictionary<string, object>? context = null)
    {
        return new AccessResult
        {
            IsAllowed = false,
            DenialReason = reason,
            Context = context ?? new Dictionary<string, object>()
        };
    }
}

/// <summary>
/// Multi-tenant data access patterns
/// </summary>
public static class TenantDataAccess
{
    /// <summary>
    /// Adds tenant ID to entity if not already set
    /// </summary>
    public static void EnsureTenantId<T>(T entity, string tenantId) where T : class
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("Tenant ID cannot be null or empty", nameof(tenantId));

        // Use reflection to set tenant_id property
        var tenantProperty = typeof(T).GetProperty("TenantId");
        if (tenantProperty != null && tenantProperty.CanWrite)
        {
            var currentValue = tenantProperty.GetValue(entity) as string;
            if (string.IsNullOrWhiteSpace(currentValue))
            {
                tenantProperty.SetValue(entity, tenantId);
            }
        }
    }

    /// <summary>
    /// Validates that entity belongs to the correct tenant
    /// </summary>
    public static bool ValidateEntityTenant<T>(T entity, string expectedTenantId) where T : class
    {
        if (string.IsNullOrWhiteSpace(expectedTenantId))
            return false;

        var tenantProperty = typeof(T).GetProperty("TenantId");
        if (tenantProperty != null && tenantProperty.CanRead)
        {
            var entityTenantId = tenantProperty.GetValue(entity) as string;
            return string.Equals(entityTenantId, expectedTenantId, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    /// <summary>
    /// Creates a tenant-aware entity filter expression
    /// </summary>
    public static Func<T, bool> CreateTenantFilter<T>(string tenantId) where T : class
    {
        return entity => ValidateEntityTenant(entity, tenantId);
    }
}