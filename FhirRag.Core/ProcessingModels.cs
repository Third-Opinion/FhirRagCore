using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace FhirRag.Core.Models;

/// <summary>
/// Represents the result of processing a FHIR resource through the RAG pipeline
/// </summary>
public class ProcessingResult
{
    [Required]
    public string ResourceId { get; set; } = string.Empty;
    
    [Required]
    public string ResourceType { get; set; } = string.Empty;
    
    [Required]
    public ProcessingStatus Status { get; set; } = ProcessingStatus.Pending;
    
    public string? ErrorMessage { get; set; }
    
    public List<ProcessingStep> Steps { get; set; } = new();
    
    public Dictionary<string, object> Metadata { get; set; } = new();
    
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime? CompletedAt { get; set; }
    
    [JsonPropertyName("tenant_id")]
    public string TenantId { get; set; } = string.Empty;
}

/// <summary>
/// Represents a step in the processing pipeline
/// </summary>
public class ProcessingStep
{
    [Required]
    public string Name { get; set; } = string.Empty;
    
    [Required]
    public ProcessingStepStatus Status { get; set; } = ProcessingStepStatus.Pending;
    
    public string? Description { get; set; }
    
    public string? ErrorMessage { get; set; }
    
    public Dictionary<string, object> Data { get; set; } = new();
    
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime? CompletedAt { get; set; }
    
    public TimeSpan Duration => CompletedAt?.Subtract(StartedAt) ?? TimeSpan.Zero;
}

/// <summary>
/// Represents enriched FHIR data with clinical NLP results and terminology mappings
/// </summary>
public class EnrichedFhirResource
{
    [Required]
    public string OriginalResourceId { get; set; } = string.Empty;
    
    [Required]
    public string ResourceType { get; set; } = string.Empty;
    
    public string? OriginalData { get; set; }
    
    public ClinicalNlpResult? NlpResult { get; set; }
    
    public List<TerminologyMapping> TerminologyMappings { get; set; } = new();
    
    public List<ClinicalRiskScore> RiskScores { get; set; } = new();
    
    public Dictionary<string, object> ExtractedFeatures { get; set; } = new();
    
    public List<float>? Embeddings { get; set; }
    
    public DateTime EnrichedAt { get; set; } = DateTime.UtcNow;
    
    [JsonPropertyName("tenant_id")]
    public string TenantId { get; set; } = string.Empty;
}

/// <summary>
/// Results from clinical NLP processing
/// </summary>
public class ClinicalNlpResult
{
    public List<MedicalEntity> Entities { get; set; } = new();
    
    public Dictionary<string, float> Confidence { get; set; } = new();
    
    public string? ProcessedText { get; set; }
    
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Medical entity extracted from clinical text
/// </summary>
public class MedicalEntity
{
    [Required]
    public string Text { get; set; } = string.Empty;
    
    [Required]
    public string Category { get; set; } = string.Empty;
    
    public string? Type { get; set; }
    
    public float Confidence { get; set; }
    
    public int BeginOffset { get; set; }
    
    public int EndOffset { get; set; }
}

/// <summary>
/// Mapping between different medical terminology systems
/// </summary>
public class TerminologyMapping
{
    [Required]
    public string SourceSystem { get; set; } = string.Empty;
    
    [Required]
    public string SourceCode { get; set; } = string.Empty;
    
    [Required]
    public string TargetSystem { get; set; } = string.Empty;
    
    [Required]
    public string TargetCode { get; set; } = string.Empty;
    
    public string? SourceDisplay { get; set; }
    
    public string? TargetDisplay { get; set; }
    
    public float Confidence { get; set; }
    
    public DateTime MappedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Clinical risk assessment score
/// </summary>
public class ClinicalRiskScore
{
    [Required]
    public string RiskType { get; set; } = string.Empty;
    
    [Required]
    public float Score { get; set; }
    
    public string? ScaleType { get; set; }
    
    public string? Interpretation { get; set; }
    
    public List<string> ContributingFactors { get; set; } = new();
    
    public float Confidence { get; set; }
    
    public DateTime CalculatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Processing status enumeration
/// </summary>
public enum ProcessingStatus
{
    Pending,
    InProgress,
    Completed,
    Failed,
    Cancelled
}

/// <summary>
/// Processing step status enumeration
/// </summary>
public enum ProcessingStepStatus
{
    Pending,
    InProgress,
    Completed,
    Failed,
    Skipped
}