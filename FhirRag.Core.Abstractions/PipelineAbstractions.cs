using FhirRag.Core.Models;
using Microsoft.Extensions.Logging;

namespace FhirRag.Core.Abstractions;

/// <summary>
/// Interface for pipeline steps
/// </summary>
public interface IPipelineStep<TInput, TOutput>
{
    /// <summary>
    /// Gets the step name
    /// </summary>
    string StepName { get; }

    /// <summary>
    /// Executes the pipeline step
    /// </summary>
    Task<TOutput> ExecuteAsync(TInput input, PipelineContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates the input before execution
    /// </summary>
    Task<bool> ValidateInputAsync(TInput input, CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface for pipeline orchestrators
/// </summary>
public interface IPipelineOrchestrator<TInput, TOutput>
{
    /// <summary>
    /// Executes the entire pipeline
    /// </summary>
    Task<TOutput> ExecutePipelineAsync(TInput input, PipelineContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a step to the pipeline
    /// </summary>
    void AddStep(IPipelineStep<object, object> step);

    /// <summary>
    /// Gets the pipeline configuration
    /// </summary>
    PipelineConfiguration Configuration { get; }
}

/// <summary>
/// Pipeline context for passing data between steps
/// </summary>
public class PipelineContext
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString();
    public string TenantId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public Dictionary<string, object> Properties { get; set; } = new();
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public List<ProcessingStep> Steps { get; set; } = new();

    public T? GetProperty<T>(string key)
    {
        return Properties.TryGetValue(key, out var value) && value is T typedValue ? typedValue : default;
    }

    public void SetProperty<T>(string key, T value)
    {
        if (value != null)
        {
            Properties[key] = value;
        }
    }
}

/// <summary>
/// Pipeline configuration
/// </summary>
public class PipelineConfiguration
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(30);
    public int MaxRetries { get; set; } = 3;
    public bool ParallelExecution { get; set; } = false;
    public Dictionary<string, object> Settings { get; set; } = new();
}

/// <summary>
/// Abstract base class for pipeline steps
/// </summary>
public abstract class BasePipelineStep<TInput, TOutput> : IPipelineStep<TInput, TOutput>
{
    protected readonly ILogger Logger;
    protected readonly ITelemetryService TelemetryService;

    protected BasePipelineStep(ILogger logger, ITelemetryService telemetryService)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        TelemetryService = telemetryService ?? throw new ArgumentNullException(nameof(telemetryService));
    }

    public abstract string StepName { get; }

    public virtual async Task<TOutput> ExecuteAsync(TInput input, PipelineContext context, CancellationToken cancellationToken = default)
    {
        var step = new ProcessingStep
        {
            Name = StepName,
            Status = ProcessingStepStatus.InProgress,
            StartedAt = DateTime.UtcNow
        };

        context.Steps.Add(step);

        try
        {
            Logger.LogInformation("Executing pipeline step: {StepName}", StepName);

            var isValid = await ValidateInputAsync(input, cancellationToken);
            if (!isValid)
            {
                throw new InvalidOperationException($"Input validation failed for step: {StepName}");
            }

            var result = await ExecuteStepAsync(input, context, cancellationToken);

            step.Status = ProcessingStepStatus.Completed;
            step.CompletedAt = DateTime.UtcNow;

            Logger.LogInformation("Completed pipeline step: {StepName}", StepName);

            await TelemetryService.RecordStepAsync(context.SessionId, step, context.TenantId, cancellationToken);

            return result;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error executing pipeline step: {StepName}", StepName);

            step.Status = ProcessingStepStatus.Failed;
            step.ErrorMessage = ex.Message;
            step.CompletedAt = DateTime.UtcNow;

            await TelemetryService.RecordStepAsync(context.SessionId, step, context.TenantId, cancellationToken);

            throw;
        }
    }

    public virtual Task<bool> ValidateInputAsync(TInput input, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(input != null);
    }

    protected abstract Task<TOutput> ExecuteStepAsync(TInput input, PipelineContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Abstract base class for pipeline orchestrators
/// </summary>
public abstract class BasePipelineOrchestrator<TInput, TOutput> : IPipelineOrchestrator<TInput, TOutput>
{
    protected readonly ILogger Logger;
    protected readonly ITelemetryService TelemetryService;
    protected readonly List<IPipelineStep<object, object>> Steps = new();

    protected BasePipelineOrchestrator(
        ILogger logger,
        ITelemetryService telemetryService,
        PipelineConfiguration configuration)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        TelemetryService = telemetryService ?? throw new ArgumentNullException(nameof(telemetryService));
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    public PipelineConfiguration Configuration { get; }

    public virtual void AddStep(IPipelineStep<object, object> step)
    {
        if (step == null)
            throw new ArgumentNullException(nameof(step));

        Steps.Add(step);
        Logger.LogDebug("Added step {StepName} to pipeline {PipelineName}", step.StepName, Configuration.Name);
    }

    public virtual async Task<TOutput> ExecutePipelineAsync(TInput input, PipelineContext context, CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("Starting pipeline execution: {PipelineName}", Configuration.Name);

        try
        {
            using var timeoutCancellation = new CancellationTokenSource(Configuration.Timeout);
            using var combinedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, timeoutCancellation.Token);

            var result = await ExecutePipelineStepsAsync(input, context, combinedCancellation.Token);

            Logger.LogInformation("Completed pipeline execution: {PipelineName}", Configuration.Name);

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Logger.LogWarning("Pipeline execution cancelled: {PipelineName}", Configuration.Name);
            throw;
        }
        catch (OperationCanceledException)
        {
            Logger.LogError("Pipeline execution timed out: {PipelineName}", Configuration.Name);
            throw new TimeoutException($"Pipeline execution timed out after {Configuration.Timeout}");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Pipeline execution failed: {PipelineName}", Configuration.Name);
            throw;
        }
    }

    protected abstract Task<TOutput> ExecutePipelineStepsAsync(TInput input, PipelineContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Interface for retry policies
/// </summary>
public interface IRetryPolicy
{
    /// <summary>
    /// Executes an operation with retry logic
    /// </summary>
    Task<T> ExecuteAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines if an exception should trigger a retry
    /// </summary>
    bool ShouldRetry(Exception exception, int attemptNumber);
}

/// <summary>
/// Exponential backoff retry policy
/// </summary>
public class ExponentialBackoffRetryPolicy : IRetryPolicy
{
    private readonly int _maxRetries;
    private readonly TimeSpan _baseDelay;
    private readonly ILogger<ExponentialBackoffRetryPolicy> _logger;

    public ExponentialBackoffRetryPolicy(int maxRetries = 3, TimeSpan? baseDelay = null, ILogger<ExponentialBackoffRetryPolicy>? logger = null)
    {
        _maxRetries = maxRetries;
        _baseDelay = baseDelay ?? TimeSpan.FromSeconds(1);
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ExponentialBackoffRetryPolicy>.Instance;
    }

    public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default)
    {
        for (int attempt = 1; attempt <= _maxRetries + 1; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (Exception ex) when (attempt <= _maxRetries && ShouldRetry(ex, attempt))
            {
                var delay = TimeSpan.FromMilliseconds(_baseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));

                _logger.LogWarning("Operation failed (attempt {Attempt}/{MaxAttempts}), retrying in {Delay}ms: {Error}",
                    attempt, _maxRetries + 1, delay.TotalMilliseconds, ex.Message);

                await Task.Delay(delay, cancellationToken);
            }
        }

        // This should never be reached due to the exception handling above
        throw new InvalidOperationException("Retry logic failed unexpectedly");
    }

    public virtual bool ShouldRetry(Exception exception, int attemptNumber)
    {
        // Don't retry on cancellation, argument exceptions, or security exceptions
        return exception is not (OperationCanceledException or ArgumentException or UnauthorizedAccessException);
    }
}