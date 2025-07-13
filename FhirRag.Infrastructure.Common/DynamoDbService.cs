using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DocumentModel;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace FhirRag.Infrastructure.Common;

/// <summary>
/// DynamoDB service for document storage and retrieval
/// </summary>
public class DynamoDbService
{
    private readonly IAmazonDynamoDB _dynamoDbClient;
    private readonly ILogger<DynamoDbService> _logger;
    private readonly DynamoDbConfiguration _configuration;
    private readonly Table _table;

    public DynamoDbService(
        IAmazonDynamoDB dynamoDbClient,
        ILogger<DynamoDbService> logger,
        IOptions<DynamoDbConfiguration> configuration)
    {
        _dynamoDbClient = dynamoDbClient;
        _logger = logger;
        _configuration = configuration.Value;
        _table = (Table)Table.LoadTable(_dynamoDbClient, _configuration.TableName, DynamoDBEntryConversion.V2);
    }

    /// <summary>
    /// Stores a document in DynamoDB
    /// </summary>
    public async Task<bool> PutDocumentAsync<T>(string partitionKey, string sortKey, T document, CancellationToken cancellationToken = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(document);
            var doc = Document.FromJson(json);

            doc[_configuration.PartitionKeyName] = partitionKey;
            doc[_configuration.SortKeyName] = sortKey;
            doc["CreatedAt"] = DateTime.UtcNow;
            doc["UpdatedAt"] = DateTime.UtcNow;

            await _table.PutItemAsync(doc, cancellationToken);

            _logger.LogDebug("Successfully stored document with PK={PartitionKey}, SK={SortKey}", partitionKey, sortKey);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store document with PK={PartitionKey}, SK={SortKey}", partitionKey, sortKey);
            return false;
        }
    }

    /// <summary>
    /// Retrieves a document from DynamoDB
    /// </summary>
    public async Task<T?> GetDocumentAsync<T>(string partitionKey, string sortKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var doc = await _table.GetItemAsync(partitionKey, sortKey, cancellationToken);

            if (doc == null)
            {
                _logger.LogDebug("Document not found with PK={PartitionKey}, SK={SortKey}", partitionKey, sortKey);
                return default;
            }

            // Remove DynamoDB metadata fields before deserialization
            doc.Remove(_configuration.PartitionKeyName);
            doc.Remove(_configuration.SortKeyName);
            doc.Remove("CreatedAt");
            doc.Remove("UpdatedAt");

            var json = doc.ToJson();
            var result = JsonSerializer.Deserialize<T>(json);

            _logger.LogDebug("Successfully retrieved document with PK={PartitionKey}, SK={SortKey}", partitionKey, sortKey);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve document with PK={PartitionKey}, SK={SortKey}", partitionKey, sortKey);
            return default;
        }
    }

    /// <summary>
    /// Deletes a document from DynamoDB
    /// </summary>
    public async Task<bool> DeleteDocumentAsync(string partitionKey, string sortKey, CancellationToken cancellationToken = default)
    {
        try
        {
            await _table.DeleteItemAsync(partitionKey, sortKey, cancellationToken);

            _logger.LogDebug("Successfully deleted document with PK={PartitionKey}, SK={SortKey}", partitionKey, sortKey);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete document with PK={PartitionKey}, SK={SortKey}", partitionKey, sortKey);
            return false;
        }
    }

    /// <summary>
    /// Queries documents by partition key
    /// </summary>
    public async Task<IEnumerable<T>> QueryDocumentsAsync<T>(string partitionKey, string? sortKeyPrefix = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var queryFilter = new QueryFilter(_configuration.PartitionKeyName, QueryOperator.Equal, partitionKey);

            if (!string.IsNullOrEmpty(sortKeyPrefix))
            {
                queryFilter.AddCondition(_configuration.SortKeyName, QueryOperator.BeginsWith, sortKeyPrefix);
            }

            var search = _table.Query(queryFilter);
            var documents = new List<T>();

            do
            {
                var documentSet = await search.GetNextSetAsync(cancellationToken);

                foreach (var doc in documentSet)
                {
                    // Remove DynamoDB metadata fields before deserialization
                    doc.Remove(_configuration.PartitionKeyName);
                    doc.Remove(_configuration.SortKeyName);
                    doc.Remove("CreatedAt");
                    doc.Remove("UpdatedAt");

                    var json = doc.ToJson();
                    var result = JsonSerializer.Deserialize<T>(json);
                    if (result != null)
                    {
                        documents.Add(result);
                    }
                }
            }
            while (!search.IsDone);

            _logger.LogDebug("Query returned {Count} documents for PK={PartitionKey}", documents.Count, partitionKey);
            return documents;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query documents for PK={PartitionKey}", partitionKey);
            return Enumerable.Empty<T>();
        }
    }

    /// <summary>
    /// Batch writes multiple documents
    /// </summary>
    public async Task<bool> BatchWriteDocumentsAsync<T>(IEnumerable<(string partitionKey, string sortKey, T document)> documents, CancellationToken cancellationToken = default)
    {
        try
        {
            var batchWrite = _table.CreateBatchWrite();

            foreach (var (partitionKey, sortKey, document) in documents)
            {
                var json = JsonSerializer.Serialize(document);
                var doc = Document.FromJson(json);

                doc[_configuration.PartitionKeyName] = partitionKey;
                doc[_configuration.SortKeyName] = sortKey;
                doc["CreatedAt"] = DateTime.UtcNow;
                doc["UpdatedAt"] = DateTime.UtcNow;

                batchWrite.AddDocumentToPut(doc);
            }

            await batchWrite.ExecuteAsync(cancellationToken);

            _logger.LogDebug("Successfully batch wrote {Count} documents", documents.Count());
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to batch write {Count} documents", documents.Count());
            return false;
        }
    }

    /// <summary>
    /// Updates a document with conditional write
    /// </summary>
    public async Task<bool> UpdateDocumentAsync<T>(string partitionKey, string sortKey, T document, string? conditionExpression = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(document);
            var doc = Document.FromJson(json);

            doc[_configuration.PartitionKeyName] = partitionKey;
            doc[_configuration.SortKeyName] = sortKey;
            doc["UpdatedAt"] = DateTime.UtcNow;

            var config = new PutItemOperationConfig();
            if (!string.IsNullOrEmpty(conditionExpression))
            {
                config.ConditionalExpression = new Expression
                {
                    ExpressionStatement = conditionExpression
                };
            }

            await _table.PutItemAsync(doc, config, cancellationToken);

            _logger.LogDebug("Successfully updated document with PK={PartitionKey}, SK={SortKey}", partitionKey, sortKey);
            return true;
        }
        catch (ConditionalCheckFailedException)
        {
            _logger.LogWarning("Conditional update failed for document with PK={PartitionKey}, SK={SortKey}", partitionKey, sortKey);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update document with PK={PartitionKey}, SK={SortKey}", partitionKey, sortKey);
            return false;
        }
    }
}

/// <summary>
/// Configuration for DynamoDB service
/// </summary>
public class DynamoDbConfiguration
{
    public string TableName { get; set; } = string.Empty;
    public string PartitionKeyName { get; set; } = "PK";
    public string SortKeyName { get; set; } = "SK";
    public string Region { get; set; } = "us-east-1";
    public int MaxRetryAttempts { get; set; } = 3;
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromMinutes(5);
}