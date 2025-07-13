using Amazon.DynamoDBv2.DocumentModel;
using System.Text.Json;

namespace FhirRag.Core.Telemetry;

/// <summary>
/// Telemetry entry for DynamoDB storage
/// </summary>
public class TelemetryEntry
{
    public string PartitionKey { get; set; } = string.Empty;
    public string SortKey { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string EntryType { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public string UserId { get; set; } = string.Empty;
    public Dictionary<string, object> Data { get; set; } = new();

    /// <summary>
    /// Converts the telemetry entry to a DynamoDB document
    /// </summary>
    public Document ToDocument()
    {
        var document = new Document
        {
            ["PartitionKey"] = PartitionKey,
            ["SortKey"] = SortKey,
            ["TenantId"] = TenantId,
            ["SessionId"] = SessionId,
            ["EntryType"] = EntryType,
            ["Timestamp"] = Timestamp,
            ["ExpiresAt"] = ((DateTimeOffset)ExpiresAt).ToUnixTimeSeconds(),
            ["UserId"] = UserId
        };

        // Serialize data as JSON string for DynamoDB
        if (Data.Any())
        {
            document["Data"] = JsonSerializer.Serialize(Data);
        }

        return document;
    }

    /// <summary>
    /// Creates a telemetry entry from a DynamoDB document
    /// </summary>
    public static TelemetryEntry FromDocument(Document document)
    {
        var entry = new TelemetryEntry
        {
            PartitionKey = document["PartitionKey"],
            SortKey = document["SortKey"],
            TenantId = document["TenantId"],
            SessionId = document["SessionId"],
            EntryType = document["EntryType"],
            Timestamp = (DateTime)document["Timestamp"],
            UserId = document["UserId"]
        };

        // Handle TTL timestamp
        if (document.TryGetValue("ExpiresAt", out var expiresAtValue) && 
            long.TryParse(expiresAtValue.ToString(), out var expiresAtUnix))
        {
            entry.ExpiresAt = DateTimeOffset.FromUnixTimeSeconds(expiresAtUnix).DateTime;
        }

        // Deserialize data from JSON
        if (document.TryGetValue("Data", out var dataValue) && !string.IsNullOrEmpty(dataValue))
        {
            try
            {
                entry.Data = JsonSerializer.Deserialize<Dictionary<string, object>>(dataValue.ToString()!) ?? new();
            }
            catch (JsonException)
            {
                entry.Data = new Dictionary<string, object> { ["raw_data"] = dataValue.ToString() };
            }
        }

        return entry;
    }
}

/// <summary>
/// Telemetry context for tracking processing sessions
/// </summary>
public class TelemetryContext
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString();
    public string TenantId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty;
    public string ResourceId { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public List<TelemetryStep> Steps { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new();
    public bool EnableS3Storage { get; set; } = true;

    /// <summary>
    /// Starts a new telemetry step
    /// </summary>
    public TelemetryStep StartStep(string stepName, string? description = null)
    {
        var step = new TelemetryStep
        {
            Name = stepName,
            Description = description ?? stepName,
            StartedAt = DateTime.UtcNow,
            SessionId = SessionId
        };

        Steps.Add(step);
        return step;
    }

    /// <summary>
    /// Completes the telemetry context
    /// </summary>
    public void Complete(bool success = true, string? errorMessage = null)
    {
        foreach (var step in Steps.Where(s => s.Status == TelemetryStepStatus.InProgress))
        {
            step.Complete(success, errorMessage);
        }
    }

    /// <summary>
    /// Gets the total processing duration
    /// </summary>
    public TimeSpan GetTotalDuration()
    {
        var completedSteps = Steps.Where(s => s.CompletedAt.HasValue).ToList();
        if (!completedSteps.Any())
            return TimeSpan.Zero;

        var earliestStart = Steps.Min(s => s.StartedAt);
        var latestEnd = completedSteps.Max(s => s.CompletedAt!.Value);
        return latestEnd - earliestStart;
    }

    /// <summary>
    /// Gets performance metrics
    /// </summary>
    public TelemetryMetrics GetMetrics()
    {
        return new TelemetryMetrics
        {
            SessionId = SessionId,
            TotalSteps = Steps.Count,
            SuccessfulSteps = Steps.Count(s => s.Status == TelemetryStepStatus.Completed),
            FailedSteps = Steps.Count(s => s.Status == TelemetryStepStatus.Failed),
            TotalDuration = GetTotalDuration(),
            AverageStepDuration = Steps.Any() ? 
                TimeSpan.FromMilliseconds(Steps.Average(s => s.Duration.TotalMilliseconds)) : 
                TimeSpan.Zero,
            ResourceType = ResourceType,
            ResourceId = ResourceId
        };
    }
}

/// <summary>
/// Individual telemetry step
/// </summary>
public class TelemetryStep
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public TelemetryStepStatus Status { get; set; } = TelemetryStepStatus.InProgress;
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public Dictionary<string, object> Data { get; set; } = new();
    public TimeSpan Duration => CompletedAt?.Subtract(StartedAt) ?? TimeSpan.Zero;

    /// <summary>
    /// Completes the step
    /// </summary>
    public void Complete(bool success = true, string? errorMessage = null)
    {
        CompletedAt = DateTime.UtcNow;
        Status = success ? TelemetryStepStatus.Completed : TelemetryStepStatus.Failed;
        ErrorMessage = errorMessage;
    }

    /// <summary>
    /// Adds data to the step
    /// </summary>
    public void AddData(string key, object value)
    {
        Data[key] = value;
    }

    /// <summary>
    /// Gets typed data from the step
    /// </summary>
    public T? GetData<T>(string key)
    {
        if (Data.TryGetValue(key, out var value) && value is T typedValue)
            return typedValue;
        return default;
    }
}

/// <summary>
/// Telemetry step status
/// </summary>
public enum TelemetryStepStatus
{
    InProgress,
    Completed,
    Failed,
    Skipped
}

/// <summary>
/// Telemetry metrics for performance analysis
/// </summary>
public class TelemetryMetrics
{
    public string SessionId { get; set; } = string.Empty;
    public int TotalSteps { get; set; }
    public int SuccessfulSteps { get; set; }
    public int FailedSteps { get; set; }
    public TimeSpan TotalDuration { get; set; }
    public TimeSpan AverageStepDuration { get; set; }
    public string ResourceType { get; set; } = string.Empty;
    public string ResourceId { get; set; } = string.Empty;
    public double SuccessRate => TotalSteps > 0 ? (double)SuccessfulSteps / TotalSteps : 0.0;
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets performance rating (1-5 stars)
    /// </summary>
    public int GetPerformanceRating()
    {
        if (TotalSteps == 0) return 0;

        // Base rating on success rate
        var rating = SuccessRate switch
        {
            >= 0.95 => 5,  // 95%+ success - 5 stars
            >= 0.85 => 4,  // 85%+ success - 4 stars  
            >= 0.70 => 3,  // 70%+ success - 3 stars
            >= 0.50 => 2,  // 50%+ success - 2 stars
            > 0.0 => 1,    // Some success - 1 star
            _ => 1         // Complete failure - 1 star
        };
        
        // Reduce rating for slow performance (only for high success rates)
        if (rating >= 4)
        {
            if (AverageStepDuration > TimeSpan.FromSeconds(30)) rating--;
            if (AverageStepDuration > TimeSpan.FromMinutes(2)) rating--;
        }
        
        return Math.Max(1, rating);
    }
}

/// <summary>
/// Training data point generated from user feedback
/// </summary>
public class TrainingDataPoint
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TenantId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Query { get; set; } = string.Empty;
    public string Response { get; set; } = string.Empty;
    public string Feedback { get; set; } = string.Empty;
    public FeedbackType FeedbackType { get; set; } = FeedbackType.Neutral;
    public float ConfidenceScore { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object> Context { get; set; } = new();
    public bool IsValidated { get; set; } = false;
    public string? ValidationNotes { get; set; }

    /// <summary>
    /// Converts to JSONL format for model training
    /// </summary>
    public string ToJsonL()
    {
        var trainingItem = new
        {
            prompt = Query,
            completion = Response,
            feedback = Feedback,
            feedback_type = FeedbackType.ToString().ToLowerInvariant(),
            confidence = ConfidenceScore,
            context = Context
        };

        return JsonSerializer.Serialize(trainingItem);
    }
}

/// <summary>
/// User feedback classification
/// </summary>
public enum FeedbackType
{
    Positive,
    Negative,
    Neutral,
    Correction,
    Enhancement
}

/// <summary>
/// Aggregated telemetry statistics
/// </summary>
public class TelemetryStatistics
{
    public string TenantId { get; set; } = string.Empty;
    public DateTime Period { get; set; }
    public TimeSpan PeriodDuration { get; set; }
    public int TotalSessions { get; set; }
    public int SuccessfulSessions { get; set; }
    public int FailedSessions { get; set; }
    public TimeSpan AverageSessionDuration { get; set; }
    public Dictionary<string, int> ResourceTypeCounts { get; set; } = new();
    public Dictionary<string, int> ErrorCounts { get; set; } = new();
    public Dictionary<string, double> PerformanceMetrics { get; set; } = new();
    public double OverallSuccessRate => TotalSessions > 0 ? (double)SuccessfulSessions / TotalSessions : 0.0;

    /// <summary>
    /// Gets the most common error
    /// </summary>
    public string? GetMostCommonError()
    {
        return ErrorCounts.Any() ? ErrorCounts.OrderByDescending(kv => kv.Value).First().Key : null;
    }

    /// <summary>
    /// Gets the most processed resource type
    /// </summary>
    public string? GetMostProcessedResourceType()
    {
        return ResourceTypeCounts.Any() ? ResourceTypeCounts.OrderByDescending(kv => kv.Value).First().Key : null;
    }
}