using FhirRag.Core.Models;
using FhirRag.Core.Extensions;
using Microsoft.Extensions.Logging;

namespace FhirRag.Core.Abstractions;

/// <summary>
/// Abstract base class for FHIR resource processors
/// </summary>
public abstract class BaseFhirProcessor<TResource> : IFhirProcessor<TResource> where TResource : class
{
    protected readonly ILogger<BaseFhirProcessor<TResource>> Logger;
    protected readonly ITelemetryService TelemetryService;

    protected BaseFhirProcessor(
        ILogger<BaseFhirProcessor<TResource>> logger,
        ITelemetryService telemetryService)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        TelemetryService = telemetryService ?? throw new ArgumentNullException(nameof(telemetryService));
    }

    public abstract string ResourceType { get; }

    public virtual async Task<ProcessingResult> ProcessAsync(TResource resource, string tenantId, CancellationToken cancellationToken = default)
    {
        var result = new ProcessingResult
        {
            ResourceId = GetResourceId(resource),
            ResourceType = ResourceType,
            TenantId = tenantId,
            Status = ProcessingStatus.InProgress
        };

        try
        {
            Logger.LogInformation("Starting processing for {ResourceType} with ID {ResourceId}", ResourceType, result.ResourceId);

            // Validate resource
            result.AddEnrichmentStep("validation", "Validating FHIR resource");
            var isValid = await ValidateAsync(resource, cancellationToken);

            if (!isValid)
            {
                result.FailStep("validation", "Resource validation failed");
                return result;
            }

            result.CompleteStep("validation", new Dictionary<string, object> { { "valid", true } });

            // Process resource
            result.AddEnrichmentStep("processing", "Processing FHIR resource");
            await ProcessResourceAsync(resource, result, cancellationToken);
            result.CompleteStep("processing");

            result.Status = ProcessingStatus.Completed;
            result.CompletedAt = DateTime.UtcNow;

            Logger.LogInformation("Completed processing for {ResourceType} with ID {ResourceId}", ResourceType, result.ResourceId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error processing {ResourceType} with ID {ResourceId}: {Error}", ResourceType, result.ResourceId, ex.Message);
            result.Status = ProcessingStatus.Failed;
            result.ErrorMessage = ex.Message;
        }
        finally
        {
            await TelemetryService.RecordResultAsync(result, cancellationToken);
        }

        return result;
    }

    public virtual Task<bool> ValidateAsync(TResource resource, CancellationToken cancellationToken = default)
    {
        // Default validation - can be overridden
        return Task.FromResult(resource != null);
    }

    protected abstract string GetResourceId(TResource resource);
    protected abstract Task ProcessResourceAsync(TResource resource, ProcessingResult result, CancellationToken cancellationToken);
}

/// <summary>
/// Abstract base class for storage services
/// </summary>
public abstract class BaseStorageService : IStorageService
{
    protected readonly ILogger<BaseStorageService> Logger;

    protected BaseStorageService(ILogger<BaseStorageService> logger)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public abstract Task<bool> StoreAsync(string key, byte[] data, Dictionary<string, string>? metadata = null, CancellationToken cancellationToken = default);
    public abstract Task<byte[]?> RetrieveAsync(string key, CancellationToken cancellationToken = default);
    public abstract Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);
    public abstract Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default);
    public abstract Task<IEnumerable<string>> ListKeysAsync(string? prefix = null, CancellationToken cancellationToken = default);

    protected virtual void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be null or empty", nameof(key));

        if (key.Length > 1024)
            throw new ArgumentException("Key cannot exceed 1024 characters", nameof(key));
    }

    protected virtual void ValidateData(byte[] data)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));

        if (data.Length == 0)
            throw new ArgumentException("Data cannot be empty", nameof(data));
    }
}

/// <summary>
/// Abstract base class for telemetry services
/// </summary>
public abstract class BaseTelemetryService : ITelemetryService
{
    protected readonly ILogger<BaseTelemetryService> Logger;

    protected BaseTelemetryService(ILogger<BaseTelemetryService> logger)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public abstract Task RecordStepAsync(string sessionId, ProcessingStep step, string tenantId, CancellationToken cancellationToken = default);
    public abstract Task<IEnumerable<ProcessingStep>> GetStepsAsync(string sessionId, string tenantId, CancellationToken cancellationToken = default);
    public abstract Task RecordResultAsync(ProcessingResult result, CancellationToken cancellationToken = default);
    public abstract Task<ProcessingResult?> GetResultAsync(string resourceId, string tenantId, CancellationToken cancellationToken = default);
    public abstract Task RecordFeedbackAsync(string sessionId, string userId, string feedback, string tenantId, CancellationToken cancellationToken = default);

    protected virtual void ValidateSessionId(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("Session ID cannot be null or empty", nameof(sessionId));
    }

    protected virtual void ValidateTenantId(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("Tenant ID cannot be null or empty", nameof(tenantId));
    }
}

/// <summary>
/// Abstract base class for embedding services
/// </summary>
public abstract class BaseEmbeddingService : IEmbeddingService
{
    protected readonly ILogger<BaseEmbeddingService> Logger;

    protected BaseEmbeddingService(ILogger<BaseEmbeddingService> logger)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public abstract Task<float[]> GenerateTextEmbeddingAsync(string text, CancellationToken cancellationToken = default);
    public abstract Task<float[]> GenerateStructuredEmbeddingAsync(Dictionary<string, object> structuredData, CancellationToken cancellationToken = default);
    public abstract Task<float[]> GenerateMultiModalEmbeddingAsync(string text, Dictionary<string, object> structuredData, CancellationToken cancellationToken = default);

    public virtual float CalculateSimilarity(float[] embedding1, float[] embedding2)
    {
        if (embedding1 == null || embedding2 == null)
            throw new ArgumentNullException("Embeddings cannot be null");

        if (embedding1.Length != embedding2.Length)
            throw new ArgumentException("Embeddings must have the same length");

        // Calculate cosine similarity
        var dotProduct = 0f;
        var norm1 = 0f;
        var norm2 = 0f;

        for (int i = 0; i < embedding1.Length; i++)
        {
            dotProduct += embedding1[i] * embedding2[i];
            norm1 += embedding1[i] * embedding1[i];
            norm2 += embedding2[i] * embedding2[i];
        }

        if (norm1 == 0f || norm2 == 0f)
            return 0f;

        return dotProduct / (MathF.Sqrt(norm1) * MathF.Sqrt(norm2));
    }

    protected virtual void ValidateText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Text cannot be null or empty", nameof(text));

        if (text.Length > 100_000)
            throw new ArgumentException("Text cannot exceed 100,000 characters", nameof(text));
    }

    protected virtual void ValidateStructuredData(Dictionary<string, object> structuredData)
    {
        if (structuredData == null)
            throw new ArgumentNullException(nameof(structuredData));

        if (structuredData.Count == 0)
            throw new ArgumentException("Structured data cannot be empty", nameof(structuredData));
    }
}

/// <summary>
/// Abstract base class for clinical services
/// </summary>
public abstract class BaseClinicalService
{
    protected readonly ILogger Logger;

    protected BaseClinicalService(ILogger logger)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected virtual void ValidateClinicalText(string clinicalText)
    {
        if (string.IsNullOrWhiteSpace(clinicalText))
            throw new ArgumentException("Clinical text cannot be null or empty", nameof(clinicalText));

        if (clinicalText.Length > 50_000)
            throw new ArgumentException("Clinical text cannot exceed 50,000 characters", nameof(clinicalText));
    }

    protected virtual void ValidatePatientId(string patientId)
    {
        if (string.IsNullOrWhiteSpace(patientId))
            throw new ArgumentException("Patient ID cannot be null or empty", nameof(patientId));
    }

    protected virtual void ValidateSystem(string system)
    {
        if (string.IsNullOrWhiteSpace(system))
            throw new ArgumentException("System cannot be null or empty", nameof(system));
    }

    protected virtual void ValidateCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Code cannot be null or empty", nameof(code));
    }
}