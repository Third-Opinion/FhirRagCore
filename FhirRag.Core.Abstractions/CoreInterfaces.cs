using FhirRag.Core.Models;
using LocalCoding = FhirRag.Core.Models.Coding;

namespace FhirRag.Core.Abstractions;

/// <summary>
/// Base interface for FHIR resource processors
/// </summary>
public interface IFhirProcessor<TResource> where TResource : class
{
    /// <summary>
    /// Processes a FHIR resource through the enrichment pipeline
    /// </summary>
    Task<ProcessingResult> ProcessAsync(TResource resource, string tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a FHIR resource before processing
    /// </summary>
    Task<bool> ValidateAsync(TResource resource, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the supported resource type
    /// </summary>
    string ResourceType { get; }
}

/// <summary>
/// Interface for storage services
/// </summary>
public interface IStorageService
{
    /// <summary>
    /// Stores data with the specified key
    /// </summary>
    Task<bool> StoreAsync(string key, byte[] data, Dictionary<string, string>? metadata = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves data by key
    /// </summary>
    Task<byte[]?> RetrieveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if data exists for the specified key
    /// </summary>
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes data by key
    /// </summary>
    Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists keys with optional prefix filter
    /// </summary>
    Task<IEnumerable<string>> ListKeysAsync(string? prefix = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface for telemetry services
/// </summary>
public interface ITelemetryService
{
    /// <summary>
    /// Records a processing step
    /// </summary>
    Task RecordStepAsync(string sessionId, ProcessingStep step, string tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets processing steps for a session
    /// </summary>
    Task<IEnumerable<ProcessingStep>> GetStepsAsync(string sessionId, string tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a processing result
    /// </summary>
    Task RecordResultAsync(ProcessingResult result, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets processing results by resource ID
    /// </summary>
    Task<ProcessingResult?> GetResultAsync(string resourceId, string tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records user feedback
    /// </summary>
    Task RecordFeedbackAsync(string sessionId, string userId, string feedback, string tenantId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface for embedding services
/// </summary>
public interface IEmbeddingService
{
    /// <summary>
    /// Generates embeddings for text
    /// </summary>
    Task<float[]> GenerateTextEmbeddingAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates embeddings for structured data
    /// </summary>
    Task<float[]> GenerateStructuredEmbeddingAsync(Dictionary<string, object> structuredData, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates multi-modal embeddings combining text and structured data
    /// </summary>
    Task<float[]> GenerateMultiModalEmbeddingAsync(string text, Dictionary<string, object> structuredData, CancellationToken cancellationToken = default);

    /// <summary>
    /// Calculates similarity between two embeddings
    /// </summary>
    float CalculateSimilarity(float[] embedding1, float[] embedding2);
}

/// <summary>
/// Interface for clinical NLP services
/// </summary>
public interface IClinicalNlpService
{
    /// <summary>
    /// Extracts medical entities from clinical text
    /// </summary>
    Task<ClinicalNlpResult> ExtractEntitiesAsync(string clinicalText, CancellationToken cancellationToken = default);

    /// <summary>
    /// Analyzes sentiment of clinical text
    /// </summary>
    Task<float> AnalyzeSentimentAsync(string clinicalText, CancellationToken cancellationToken = default);

    /// <summary>
    /// Detects clinical concepts and relationships
    /// </summary>
    Task<IEnumerable<string>> DetectConceptsAsync(string clinicalText, CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface for terminology mapping services
/// </summary>
public interface ITerminologyService
{
    /// <summary>
    /// Maps codes between terminology systems
    /// </summary>
    Task<IEnumerable<TerminologyMapping>> MapCodeAsync(string sourceSystem, string sourceCode, string targetSystem, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a code in a terminology system
    /// </summary>
    Task<bool> ValidateCodeAsync(string system, string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches for codes by display text
    /// </summary>
    Task<IEnumerable<LocalCoding>> SearchCodesAsync(string system, string searchText, int maxResults = 10, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the display text for a code
    /// </summary>
    Task<string?> GetDisplayTextAsync(string system, string code, CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface for risk assessment services
/// </summary>
public interface IRiskAssessmentService
{
    /// <summary>
    /// Calculates clinical risk scores for a patient
    /// </summary>
    Task<IEnumerable<ClinicalRiskScore>> CalculateRiskScoresAsync(string patientId, string tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Calculates a specific risk score type
    /// </summary>
    Task<ClinicalRiskScore?> CalculateSpecificRiskAsync(string patientId, string riskType, string tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets available risk assessment types
    /// </summary>
    Task<IEnumerable<string>> GetAvailableRiskTypesAsync(CancellationToken cancellationToken = default);
}
