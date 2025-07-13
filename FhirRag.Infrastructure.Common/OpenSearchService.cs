using Amazon.OpenSearchService;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;

namespace FhirRag.Infrastructure.Common;

/// <summary>
/// OpenSearch service for document indexing and search operations
/// </summary>
public class OpenSearchService
{
    private readonly IAmazonOpenSearchService _openSearchClient;
    private readonly ILogger<OpenSearchService> _logger;
    private readonly OpenSearchConfiguration _configuration;
    private readonly HttpClient _httpClient;

    public OpenSearchService(
        IAmazonOpenSearchService openSearchClient,
        ILogger<OpenSearchService> logger,
        IOptions<OpenSearchConfiguration> configuration,
        HttpClient httpClient)
    {
        _openSearchClient = openSearchClient;
        _logger = logger;
        _configuration = configuration.Value;
        _httpClient = httpClient;
    }

    /// <summary>
    /// Indexes a document in OpenSearch
    /// </summary>
    public async Task<bool> IndexDocumentAsync<T>(string index, string documentId, T document, CancellationToken cancellationToken = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(document);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PutAsync($"{_configuration.Endpoint}/{index}/_doc/{documentId}", content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Successfully indexed document {DocumentId} in index {Index}", documentId, index);
                return true;
            }

            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Failed to index document {DocumentId} in index {Index}. Status: {StatusCode}, Error: {Error}",
                documentId, index, response.StatusCode, errorContent);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while indexing document {DocumentId} in index {Index}", documentId, index);
            return false;
        }
    }

    /// <summary>
    /// Searches for documents using query string
    /// </summary>
    public async Task<OpenSearchResult<T>> SearchAsync<T>(string index, OpenSearchQuery query, CancellationToken cancellationToken = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(query);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{_configuration.Endpoint}/{index}/_search", content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
                var searchResponse = JsonSerializer.Deserialize<OpenSearchResponse<T>>(responseContent);

                if (searchResponse != null)
                {
                    var result = new OpenSearchResult<T>
                    {
                        Documents = searchResponse.Hits.Hits.Select(h => h.Source).ToList(),
                        TotalHits = searchResponse.Hits.Total.Value,
                        MaxScore = searchResponse.Hits.MaxScore,
                        Took = searchResponse.Took
                    };

                    _logger.LogDebug("Search in index {Index} returned {Count} documents", index, result.Documents.Count);
                    return result;
                }
            }

            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Search failed in index {Index}. Status: {StatusCode}, Error: {Error}",
                index, response.StatusCode, errorContent);

            return new OpenSearchResult<T>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during search in index {Index}", index);
            return new OpenSearchResult<T>();
        }
    }

    /// <summary>
    /// Performs vector similarity search using embeddings
    /// </summary>
    public async Task<OpenSearchResult<T>> VectorSearchAsync<T>(string index, float[] queryVector, int size = 10, CancellationToken cancellationToken = default)
    {
        try
        {
            var vectorQuery = new
            {
                size = size,
                query = new
                {
                    knn = new
                    {
                        embedding = new
                        {
                            vector = queryVector,
                            k = size
                        }
                    }
                }
            };

            var json = JsonSerializer.Serialize(vectorQuery);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{_configuration.Endpoint}/{index}/_search", content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
                var searchResponse = JsonSerializer.Deserialize<OpenSearchResponse<T>>(responseContent);

                if (searchResponse != null)
                {
                    var result = new OpenSearchResult<T>
                    {
                        Documents = searchResponse.Hits.Hits.Select(h => h.Source).ToList(),
                        TotalHits = searchResponse.Hits.Total.Value,
                        MaxScore = searchResponse.Hits.MaxScore,
                        Took = searchResponse.Took
                    };

                    _logger.LogDebug("Vector search in index {Index} returned {Count} documents", index, result.Documents.Count);
                    return result;
                }
            }

            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Vector search failed in index {Index}. Status: {StatusCode}, Error: {Error}",
                index, response.StatusCode, errorContent);

            return new OpenSearchResult<T>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during vector search in index {Index}", index);
            return new OpenSearchResult<T>();
        }
    }

    /// <summary>
    /// Bulk indexes multiple documents
    /// </summary>
    public async Task<bool> BulkIndexAsync<T>(string index, IEnumerable<(string id, T document)> documents, CancellationToken cancellationToken = default)
    {
        try
        {
            var bulkBody = new StringBuilder();

            foreach (var (id, document) in documents)
            {
                var indexAction = new { index = new { _index = index, _id = id } };
                bulkBody.AppendLine(JsonSerializer.Serialize(indexAction));
                bulkBody.AppendLine(JsonSerializer.Serialize(document));
            }

            var content = new StringContent(bulkBody.ToString(), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync($"{_configuration.Endpoint}/_bulk", content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Successfully bulk indexed {Count} documents in index {Index}", documents.Count(), index);
                return true;
            }

            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Bulk index failed in index {Index}. Status: {StatusCode}, Error: {Error}",
                index, response.StatusCode, errorContent);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during bulk index in index {Index}", index);
            return false;
        }
    }

    /// <summary>
    /// Creates an index with specified mapping
    /// </summary>
    public async Task<bool> CreateIndexAsync(string index, object mapping, CancellationToken cancellationToken = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(mapping);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PutAsync($"{_configuration.Endpoint}/{index}", content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Successfully created index {Index}", index);
                return true;
            }

            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Failed to create index {Index}. Status: {StatusCode}, Error: {Error}",
                index, response.StatusCode, errorContent);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while creating index {Index}", index);
            return false;
        }
    }
}

/// <summary>
/// Configuration for OpenSearch service
/// </summary>
public class OpenSearchConfiguration
{
    public string Endpoint { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool UseHttps { get; set; } = true;
    public int MaxRetryAttempts { get; set; } = 3;
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>
/// OpenSearch query model
/// </summary>
public class OpenSearchQuery
{
    public int Size { get; set; } = 10;
    public int From { get; set; } = 0;
    public object Query { get; set; } = new object();
    public object[]? Sort { get; set; }
    public string[]? Source { get; set; }
}

/// <summary>
/// OpenSearch search result
/// </summary>
public class OpenSearchResult<T>
{
    public List<T> Documents { get; set; } = new();
    public long TotalHits { get; set; }
    public double? MaxScore { get; set; }
    public int Took { get; set; }
}

/// <summary>
/// Internal OpenSearch response models
/// </summary>
internal class OpenSearchResponse<T>
{
    public int Took { get; set; }
    public OpenSearchHits<T> Hits { get; set; } = new();
}

internal class OpenSearchHits<T>
{
    public OpenSearchTotal Total { get; set; } = new();
    public double? MaxScore { get; set; }
    public List<OpenSearchHit<T>> Hits { get; set; } = new();
}

internal class OpenSearchTotal
{
    public long Value { get; set; }
    public string Relation { get; set; } = string.Empty;
}

internal class OpenSearchHit<T>
{
    public string Index { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public double? Score { get; set; }
    public T Source { get; set; } = default!;
}