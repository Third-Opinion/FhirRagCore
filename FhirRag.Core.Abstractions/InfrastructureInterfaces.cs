using FhirRag.Core.Models;

namespace FhirRag.Core.Abstractions;

/// <summary>
/// Interface for Large Language Model services
/// </summary>
public interface ILlmService
{
    /// <summary>
    /// Generates a response from the LLM
    /// </summary>
    Task<LlmResponse> GenerateResponseAsync(LlmRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates embeddings for text
    /// </summary>
    Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if the LLM service is healthy
    /// </summary>
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface for workflow orchestration services
/// </summary>
public interface IOrchestrationService
{
    /// <summary>
    /// Starts a new workflow
    /// </summary>
    Task<OrchestrationResult> StartWorkflowAsync(WorkflowRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the status of a workflow
    /// </summary>
    Task<OrchestrationResult> GetWorkflowStatusAsync(string workflowId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a running workflow
    /// </summary>
    Task<bool> CancelWorkflowAsync(string workflowId, CancellationToken cancellationToken = default);
}

/// <summary>
/// LLM request model
/// </summary>
public class LlmRequest
{
    public string Prompt { get; set; } = string.Empty;
    public string? SystemPrompt { get; set; }
    public string? ModelId { get; set; }
    public int? MaxTokens { get; set; }
    public double? Temperature { get; set; }
    public double? TopP { get; set; }
    public string[]? StopSequences { get; set; }
    public Dictionary<string, object> Parameters { get; set; } = new();
}

/// <summary>
/// LLM response model
/// </summary>
public class LlmResponse
{
    public string Content { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public int TokensUsed { get; set; }
    public string FinishReason { get; set; } = string.Empty;
    public Dictionary<string, object> Metadata { get; set; } = new();
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Workflow request model
/// </summary>
public class WorkflowRequest
{
    public string WorkflowType { get; set; } = string.Empty;
    public string ResourceId { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty;
    public Dictionary<string, object> Parameters { get; set; } = new();
    public int Priority { get; set; } = 0;
    public DateTime ScheduledAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Orchestration result model
/// </summary>
public class OrchestrationResult
{
    public string WorkflowId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public List<WorkflowStep> Steps { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// Workflow step model
/// </summary>
public class WorkflowStep
{
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? QueuedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public Dictionary<string, object> Data { get; set; } = new();
}