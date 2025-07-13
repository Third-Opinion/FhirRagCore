using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DocumentModel;
using Amazon.DynamoDBv2.Model;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using FhirRag.Core.Models;
using FhirRag.Core.Abstractions;
using FhirRag.Core.Security;

namespace FhirRag.Core.Telemetry;

/// <summary>
/// Configuration for DynamoDB telemetry service
/// </summary>
public class DynamoDbTelemetryConfiguration
{
    public string TableName { get; set; } = "fhir-rag-telemetry";
    public string S3BucketName { get; set; } = "fhir-rag-telemetry-data";
    public string Region { get; set; } = "us-east-1";
    public int TelemetryTtlDays { get; set; } = 90;
    public int FeedbackTtlDays { get; set; } = 365;
    public int MaxPayloadSizeBytes { get; set; } = 100_000; // 100KB
    public bool EnableS3StorageForLargeData { get; set; } = true;
    public string? AccessKeyId { get; set; }
    public string? SecretAccessKey { get; set; }
}

/// <summary>
/// DynamoDB-based telemetry service implementation
/// </summary>
public class DynamoDbTelemetryService : BaseTelemetryService
{
    private readonly DynamoDbTelemetryConfiguration _configuration;
    private readonly IAmazonDynamoDB _dynamoDbClient;
    private readonly IAmazonS3? _s3Client;
    private readonly SecurityContextProvider _securityContextProvider;
    private readonly Table _table;

    public DynamoDbTelemetryService(
        DynamoDbTelemetryConfiguration configuration,
        IAmazonDynamoDB dynamoDbClient,
        ILogger<DynamoDbTelemetryService> logger,
        SecurityContextProvider securityContextProvider,
        IAmazonS3? s3Client = null) : base(logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _dynamoDbClient = dynamoDbClient ?? throw new ArgumentNullException(nameof(dynamoDbClient));
        _securityContextProvider = securityContextProvider ?? throw new ArgumentNullException(nameof(securityContextProvider));
        _s3Client = s3Client;
        _table = Table.LoadTable(_dynamoDbClient, _configuration.TableName);
    }

    public override async Task RecordStepAsync(string sessionId, ProcessingStep step, string tenantId, CancellationToken cancellationToken = default)
    {
        ValidateSessionId(sessionId);
        ValidateTenantId(tenantId);

        if (step == null)
            throw new ArgumentNullException(nameof(step));

        try
        {
            var telemetryEntry = new TelemetryEntry
            {
                PartitionKey = $"STEP#{tenantId}#{sessionId}",
                SortKey = $"STEP#{DateTime.UtcNow:yyyyMMddHHmmss}#{Guid.NewGuid():N}",
                TenantId = tenantId,
                SessionId = sessionId,
                EntryType = "ProcessingStep",
                Timestamp = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(_configuration.TelemetryTtlDays),
                UserId = _securityContextProvider.Current?.UserId ?? "system",
                Data = new Dictionary<string, object>
                {
                    ["step_name"] = step.Name,
                    ["status"] = step.Status.ToString(),
                    ["started_at"] = step.StartedAt,
                    ["completed_at"] = step.CompletedAt,
                    ["duration_ms"] = step.Duration.TotalMilliseconds,
                    ["error_message"] = step.ErrorMessage ?? string.Empty
                }
            };

            // Handle large step data by storing in S3
            if (step.Data.Any())
            {
                await HandleLargeDataAsync(telemetryEntry, step.Data, cancellationToken);
            }

            await StoreEntryAsync(telemetryEntry, cancellationToken);

            Logger.LogDebug("Recorded processing step {StepName} for session {SessionId} in tenant {TenantId}",
                step.Name, sessionId, tenantId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error recording processing step {StepName} for session {SessionId}",
                step.Name, sessionId);
            throw;
        }
    }

    public override async Task<IEnumerable<ProcessingStep>> GetStepsAsync(string sessionId, string tenantId, CancellationToken cancellationToken = default)
    {
        ValidateSessionId(sessionId);
        ValidateTenantId(tenantId);

        try
        {
            var partitionKey = $"STEP#{tenantId}#{sessionId}";
            var queryFilter = new QueryFilter("PartitionKey", QueryOperator.Equal, partitionKey);

            var search = _table.Query(queryFilter);
            var entries = new List<TelemetryEntry>();

            do
            {
                var documents = await search.GetNextSetAsync(cancellationToken);
                entries.AddRange(documents.Select(doc => TelemetryEntry.FromDocument(doc)));
            } while (!search.IsDone);

            var steps = new List<ProcessingStep>();

            foreach (var entry in entries.Where(e => e.EntryType == "ProcessingStep"))
            {
                var step = new ProcessingStep
                {
                    Name = entry.Data.GetValueOrDefault("step_name")?.ToString() ?? string.Empty,
                    Status = Enum.TryParse<ProcessingStepStatus>(
                        entry.Data.GetValueOrDefault("status")?.ToString(), out var status) ? status : ProcessingStepStatus.Pending,
                    StartedAt = entry.Data.GetValueOrDefault("started_at") is DateTime startedAt ? startedAt : DateTime.MinValue,
                    CompletedAt = entry.Data.GetValueOrDefault("completed_at") is DateTime completedAt ? completedAt : null,
                    ErrorMessage = entry.Data.GetValueOrDefault("error_message")?.ToString()
                };

                // Load large data from S3 if necessary
                if (entry.Data.ContainsKey("s3_key"))
                {
                    var s3Data = await LoadLargeDataFromS3Async(entry.Data["s3_key"].ToString()!, cancellationToken);
                    if (s3Data != null)
                    {
                        step.Data = s3Data;
                    }
                }

                steps.Add(step);
            }

            Logger.LogDebug("Retrieved {StepCount} processing steps for session {SessionId}",
                steps.Count, sessionId);

            return steps.OrderBy(s => s.StartedAt);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error retrieving processing steps for session {SessionId}", sessionId);
            throw;
        }
    }

    public override async Task RecordResultAsync(ProcessingResult result, CancellationToken cancellationToken = default)
    {
        if (result == null)
            throw new ArgumentNullException(nameof(result));

        ValidateTenantId(result.TenantId);

        try
        {
            var telemetryEntry = new TelemetryEntry
            {
                PartitionKey = $"RESULT#{result.TenantId}#{result.ResourceId}",
                SortKey = $"RESULT#{DateTime.UtcNow:yyyyMMddHHmmss}",
                TenantId = result.TenantId,
                SessionId = Guid.NewGuid().ToString(),
                EntryType = "ProcessingResult",
                Timestamp = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(_configuration.TelemetryTtlDays),
                UserId = _securityContextProvider.Current?.UserId ?? "system",
                Data = new Dictionary<string, object>
                {
                    ["resource_id"] = result.ResourceId,
                    ["resource_type"] = result.ResourceType,
                    ["status"] = result.Status.ToString(),
                    ["started_at"] = result.StartedAt,
                    ["completed_at"] = result.CompletedAt,
                    ["error_message"] = result.ErrorMessage ?? string.Empty,
                    ["step_count"] = result.Steps.Count
                }
            };

            // Handle large metadata
            if (result.Metadata.Any())
            {
                await HandleLargeDataAsync(telemetryEntry, result.Metadata, cancellationToken);
            }

            await StoreEntryAsync(telemetryEntry, cancellationToken);

            Logger.LogDebug("Recorded processing result for resource {ResourceId} in tenant {TenantId}",
                result.ResourceId, result.TenantId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error recording processing result for resource {ResourceId}", result.ResourceId);
            throw;
        }
    }

    public override async Task<ProcessingResult?> GetResultAsync(string resourceId, string tenantId, CancellationToken cancellationToken = default)
    {
        ValidateTenantId(tenantId);

        if (string.IsNullOrWhiteSpace(resourceId))
            throw new ArgumentException("Resource ID cannot be null or empty", nameof(resourceId));

        try
        {
            var partitionKey = $"RESULT#{tenantId}#{resourceId}";
            var queryFilter = new QueryFilter("PartitionKey", QueryOperator.Equal, partitionKey);
            queryFilter.AddCondition("SortKey", QueryOperator.BeginsWith, "RESULT#");

            var search = _table.Query(queryFilter);

            var documents = await search.GetNextSetAsync(cancellationToken);
            var entry = documents.FirstOrDefault();

            if (entry == null)
            {
                Logger.LogDebug("No processing result found for resource {ResourceId} in tenant {TenantId}",
                    resourceId, tenantId);
                return null;
            }

            var telemetryEntry = TelemetryEntry.FromDocument(entry);

            var result = new ProcessingResult
            {
                ResourceId = telemetryEntry.Data.GetValueOrDefault("resource_id")?.ToString() ?? string.Empty,
                ResourceType = telemetryEntry.Data.GetValueOrDefault("resource_type")?.ToString() ?? string.Empty,
                Status = Enum.TryParse<ProcessingStatus>(
                    telemetryEntry.Data.GetValueOrDefault("status")?.ToString(), out var status) ? status : ProcessingStatus.Pending,
                StartedAt = telemetryEntry.Data.GetValueOrDefault("started_at") is DateTime startedAt ? startedAt : DateTime.MinValue,
                CompletedAt = telemetryEntry.Data.GetValueOrDefault("completed_at") is DateTime completedAt ? completedAt : null,
                ErrorMessage = telemetryEntry.Data.GetValueOrDefault("error_message")?.ToString(),
                TenantId = telemetryEntry.TenantId
            };

            // Load large metadata from S3 if necessary
            if (telemetryEntry.Data.ContainsKey("s3_key"))
            {
                var s3Data = await LoadLargeDataFromS3Async(telemetryEntry.Data["s3_key"].ToString()!, cancellationToken);
                if (s3Data != null)
                {
                    result.Metadata = s3Data;
                }
            }

            Logger.LogDebug("Retrieved processing result for resource {ResourceId}", resourceId);

            return result;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error retrieving processing result for resource {ResourceId}", resourceId);
            throw;
        }
    }

    public override async Task RecordFeedbackAsync(string sessionId, string userId, string feedback, string tenantId, CancellationToken cancellationToken = default)
    {
        ValidateSessionId(sessionId);
        ValidateTenantId(tenantId);

        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User ID cannot be null or empty", nameof(userId));

        if (string.IsNullOrWhiteSpace(feedback))
            throw new ArgumentException("Feedback cannot be null or empty", nameof(feedback));

        try
        {
            var telemetryEntry = new TelemetryEntry
            {
                PartitionKey = $"FEEDBACK#{tenantId}#{userId}",
                SortKey = $"FEEDBACK#{DateTime.UtcNow:yyyyMMddHHmmss}#{Guid.NewGuid():N}",
                TenantId = tenantId,
                SessionId = sessionId,
                EntryType = "UserFeedback",
                Timestamp = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(_configuration.FeedbackTtlDays),
                UserId = userId,
                Data = new Dictionary<string, object>
                {
                    ["feedback"] = feedback,
                    ["session_id"] = sessionId,
                    ["feedback_length"] = feedback.Length
                }
            };

            await StoreEntryAsync(telemetryEntry, cancellationToken);

            Logger.LogDebug("Recorded user feedback from {UserId} for session {SessionId} in tenant {TenantId}",
                userId, sessionId, tenantId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error recording user feedback from {UserId}", userId);
            throw;
        }
    }

    /// <summary>
    /// Handles large data by storing it in S3 if it exceeds the size threshold
    /// </summary>
    private async Task HandleLargeDataAsync(TelemetryEntry entry, Dictionary<string, object> data, CancellationToken cancellationToken)
    {
        if (!_configuration.EnableS3StorageForLargeData || _s3Client == null)
        {
            entry.Data["large_data"] = data;
            return;
        }

        var serializedData = JsonSerializer.Serialize(data);

        if (serializedData.Length > _configuration.MaxPayloadSizeBytes)
        {
            var s3Key = $"{entry.TenantId}/{entry.EntryType}/{DateTime.UtcNow:yyyy/MM/dd}/{Guid.NewGuid():N}.json";

            await _s3Client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = _configuration.S3BucketName,
                Key = s3Key,
                ContentBody = serializedData,
                ContentType = "application/json",
                ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256
            }, cancellationToken);

            entry.Data["s3_key"] = s3Key;
            entry.Data["data_size_bytes"] = serializedData.Length;

            Logger.LogDebug("Stored large telemetry data ({Size} bytes) in S3 at key {S3Key}",
                serializedData.Length, s3Key);
        }
        else
        {
            entry.Data["large_data"] = data;
        }
    }

    /// <summary>
    /// Loads large data from S3
    /// </summary>
    private async Task<Dictionary<string, object>?> LoadLargeDataFromS3Async(string s3Key, CancellationToken cancellationToken)
    {
        if (_s3Client == null || string.IsNullOrWhiteSpace(s3Key))
            return null;

        try
        {
            var response = await _s3Client.GetObjectAsync(new GetObjectRequest
            {
                BucketName = _configuration.S3BucketName,
                Key = s3Key
            }, cancellationToken);

            using var reader = new StreamReader(response.ResponseStream);
            var json = await reader.ReadToEndAsync(cancellationToken);

            var data = JsonSerializer.Deserialize<Dictionary<string, object>>(json);

            Logger.LogDebug("Loaded large telemetry data from S3 key {S3Key}", s3Key);

            return data;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to load large telemetry data from S3 key {S3Key}", s3Key);
            return null;
        }
    }

    /// <summary>
    /// Stores a telemetry entry in DynamoDB
    /// </summary>
    private async Task StoreEntryAsync(TelemetryEntry entry, CancellationToken cancellationToken)
    {
        var document = entry.ToDocument();
        await _table.PutItemAsync(document, cancellationToken);
    }
}
