using Microsoft.Extensions.Logging;
using FhirRag.Core.Abstractions;
using FhirRag.Core.Security;

namespace FhirRag.Core.Telemetry;

/// <summary>
/// Provider for managing telemetry contexts across request scopes
/// </summary>
public class TelemetryContextProvider
{
    private readonly ILogger<TelemetryContextProvider> _logger;
    private readonly ITelemetryService _telemetryService;
    private readonly SecurityContextProvider _securityContextProvider;
    private readonly Dictionary<string, TelemetryContext> _activeContexts = new();
    private readonly object _lock = new object();

    public TelemetryContextProvider(
        ILogger<TelemetryContextProvider> logger,
        ITelemetryService telemetryService,
        SecurityContextProvider securityContextProvider)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _telemetryService = telemetryService ?? throw new ArgumentNullException(nameof(telemetryService));
        _securityContextProvider = securityContextProvider ?? throw new ArgumentNullException(nameof(securityContextProvider));
    }

    /// <summary>
    /// Creates a new telemetry context
    /// </summary>
    public TelemetryContext CreateContext(string resourceType, string resourceId)
    {
        var securityContext = _securityContextProvider.Current;
        if (securityContext == null)
            throw new InvalidOperationException("Security context must be set before creating telemetry context");

        var context = new TelemetryContext
        {
            TenantId = securityContext.TenantId,
            UserId = securityContext.UserId,
            ResourceType = resourceType,
            ResourceId = resourceId
        };

        lock (_lock)
        {
            _activeContexts[context.SessionId] = context;
        }

        _logger.LogDebug("Created telemetry context {SessionId} for resource {ResourceType}/{ResourceId}",
            context.SessionId, resourceType, resourceId);

        return context;
    }

    /// <summary>
    /// Gets an active telemetry context by session ID
    /// </summary>
    public TelemetryContext? GetContext(string sessionId)
    {
        lock (_lock)
        {
            return _activeContexts.TryGetValue(sessionId, out var context) ? context : null;
        }
    }

    /// <summary>
    /// Completes and persists a telemetry context
    /// </summary>
    public async Task CompleteContextAsync(string sessionId, bool success = true, string? errorMessage = null, CancellationToken cancellationToken = default)
    {
        TelemetryContext? context;
        
        lock (_lock)
        {
            if (!_activeContexts.TryGetValue(sessionId, out context))
            {
                _logger.LogWarning("Attempted to complete non-existent telemetry context {SessionId}", sessionId);
                return;
            }
            
            _activeContexts.Remove(sessionId);
        }

        try
        {
            context.Complete(success, errorMessage);

            // Persist all steps to telemetry service
            foreach (var step in context.Steps)
            {
                var processingStep = new FhirRag.Core.Models.ProcessingStep
                {
                    Name = step.Name,
                    Description = step.Description,
                    Status = step.Status switch
                    {
                        TelemetryStepStatus.InProgress => FhirRag.Core.Models.ProcessingStepStatus.InProgress,
                        TelemetryStepStatus.Completed => FhirRag.Core.Models.ProcessingStepStatus.Completed,
                        TelemetryStepStatus.Failed => FhirRag.Core.Models.ProcessingStepStatus.Failed,
                        TelemetryStepStatus.Skipped => FhirRag.Core.Models.ProcessingStepStatus.Skipped,
                        _ => FhirRag.Core.Models.ProcessingStepStatus.Pending
                    },
                    StartedAt = step.StartedAt,
                    CompletedAt = step.CompletedAt,
                    ErrorMessage = step.ErrorMessage,
                    Data = step.Data
                };

                await _telemetryService.RecordStepAsync(sessionId, processingStep, context.TenantId, cancellationToken);
            }

            _logger.LogDebug("Completed and persisted telemetry context {SessionId} with {StepCount} steps",
                sessionId, context.Steps.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error completing telemetry context {SessionId}", sessionId);
            throw;
        }
    }

    /// <summary>
    /// Starts a new step in the specified context
    /// </summary>
    public async Task<TelemetryStep> StartStepAsync(string sessionId, string stepName, string? description = null)
    {
        var context = GetContext(sessionId);
        if (context == null)
            throw new InvalidOperationException($"Telemetry context {sessionId} not found");

        var step = context.StartStep(stepName, description);
        
        _logger.LogDebug("Started telemetry step {StepName} in session {SessionId}", stepName, sessionId);
        
        return step;
    }

    /// <summary>
    /// Completes a step in the specified context
    /// </summary>
    public async Task CompleteStepAsync(string sessionId, string stepName, bool success = true, string? errorMessage = null, Dictionary<string, object>? data = null)
    {
        var context = GetContext(sessionId);
        if (context == null)
        {
            _logger.LogWarning("Attempted to complete step {StepName} in non-existent context {SessionId}", stepName, sessionId);
            return;
        }

        var step = context.Steps.FirstOrDefault(s => s.Name == stepName && s.Status == TelemetryStepStatus.InProgress);
        if (step == null)
        {
            _logger.LogWarning("Attempted to complete non-existent or already completed step {StepName} in session {SessionId}", stepName, sessionId);
            return;
        }

        step.Complete(success, errorMessage);
        
        if (data != null)
        {
            foreach (var kvp in data)
            {
                step.AddData(kvp.Key, kvp.Value);
            }
        }

        _logger.LogDebug("Completed telemetry step {StepName} in session {SessionId} - Success: {Success}",
            stepName, sessionId, success);
    }

    /// <summary>
    /// Gets metrics for all active contexts
    /// </summary>
    public IEnumerable<TelemetryMetrics> GetActiveMetrics()
    {
        lock (_lock)
        {
            return _activeContexts.Values.Select(c => c.GetMetrics()).ToList();
        }
    }

    /// <summary>
    /// Cleans up expired contexts (should be called periodically)
    /// </summary>
    public async Task CleanupExpiredContextsAsync(TimeSpan maxAge, CancellationToken cancellationToken = default)
    {
        var cutoffTime = DateTime.UtcNow - maxAge;
        var expiredSessionIds = new List<string>();

        lock (_lock)
        {
            foreach (var kvp in _activeContexts)
            {
                if (kvp.Value.StartedAt < cutoffTime)
                {
                    expiredSessionIds.Add(kvp.Key);
                }
            }
        }

        foreach (var sessionId in expiredSessionIds)
        {
            try
            {
                await CompleteContextAsync(sessionId, false, "Context expired", cancellationToken);
                _logger.LogInformation("Cleaned up expired telemetry context {SessionId}", sessionId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error cleaning up expired telemetry context {SessionId}", sessionId);
            }
        }

        if (expiredSessionIds.Any())
        {
            _logger.LogDebug("Cleaned up {ExpiredCount} expired telemetry contexts", expiredSessionIds.Count);
        }
    }
}

/// <summary>
/// Service for analyzing telemetry data and generating insights
/// </summary>
public class TelemetryAnalyticsService
{
    private readonly ITelemetryService _telemetryService;
    private readonly ILogger<TelemetryAnalyticsService> _logger;

    public TelemetryAnalyticsService(
        ITelemetryService telemetryService,
        ILogger<TelemetryAnalyticsService> logger)
    {
        _telemetryService = telemetryService ?? throw new ArgumentNullException(nameof(telemetryService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Generates telemetry statistics for a given period
    /// </summary>
    public async Task<TelemetryStatistics> GenerateStatisticsAsync(
        string tenantId, 
        DateTime fromDate, 
        DateTime toDate, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            var statistics = new TelemetryStatistics
            {
                TenantId = tenantId,
                Period = fromDate,
                PeriodDuration = toDate - fromDate
            };

            // This would typically query the telemetry service for aggregated data
            // For now, we'll return a basic structure
            // In a real implementation, this would involve complex queries against DynamoDB/S3

            _logger.LogDebug("Generated telemetry statistics for tenant {TenantId} from {FromDate} to {ToDate}",
                tenantId, fromDate, toDate);

            return statistics;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating telemetry statistics for tenant {TenantId}", tenantId);
            throw;
        }
    }

    /// <summary>
    /// Identifies performance bottlenecks from telemetry data
    /// </summary>
    public async Task<IEnumerable<PerformanceInsight>> IdentifyBottlenecksAsync(
        string tenantId,
        TimeSpan lookbackPeriod,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var insights = new List<PerformanceInsight>();

            // Analyze processing step durations, error rates, etc.
            // This would involve sophisticated analysis of telemetry data

            _logger.LogDebug("Identified performance bottlenecks for tenant {TenantId}", tenantId);

            return insights;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error identifying performance bottlenecks for tenant {TenantId}", tenantId);
            throw;
        }
    }

    /// <summary>
    /// Generates training data from negative feedback
    /// </summary>
    public async Task<IEnumerable<TrainingDataPoint>> GenerateTrainingDataAsync(
        string tenantId,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var trainingData = new List<TrainingDataPoint>();

            // Extract training data from feedback entries
            // This would involve querying feedback telemetry and processing it

            _logger.LogDebug("Generated {TrainingDataCount} training data points for tenant {TenantId}",
                trainingData.Count, tenantId);

            return trainingData;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating training data for tenant {TenantId}", tenantId);
            throw;
        }
    }
}

/// <summary>
/// Performance insight from telemetry analysis
/// </summary>
public class PerformanceInsight
{
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Severity { get; set; } = "Medium";
    public Dictionary<string, object> Data { get; set; } = new();
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
    public string? RecommendedAction { get; set; }
}

/// <summary>
/// Extension methods for telemetry integration
/// </summary>
public static class TelemetryExtensions
{
    /// <summary>
    /// Executes an operation with automatic telemetry tracking
    /// </summary>
    public static async Task<T> WithTelemetryAsync<T>(
        this TelemetryContextProvider provider,
        string sessionId,
        string stepName,
        Func<Task<T>> operation,
        string? description = null)
    {
        var step = await provider.StartStepAsync(sessionId, stepName, description);
        
        try
        {
            var result = await operation();
            await provider.CompleteStepAsync(sessionId, stepName, true);
            return result;
        }
        catch (Exception ex)
        {
            await provider.CompleteStepAsync(sessionId, stepName, false, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Executes an operation with automatic telemetry tracking (void return)
    /// </summary>
    public static async Task WithTelemetryAsync(
        this TelemetryContextProvider provider,
        string sessionId,
        string stepName,
        Func<Task> operation,
        string? description = null)
    {
        await provider.WithTelemetryAsync(sessionId, stepName, async () =>
        {
            await operation();
            return 0; // Dummy return value
        }, description);
    }
}