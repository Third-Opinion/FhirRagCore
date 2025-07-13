using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging;

namespace FhirRag.Core.Telemetry;

/// <summary>
/// Utility for setting up DynamoDB tables for telemetry
/// </summary>
public class TelemetryTableSetup
{
    private readonly IAmazonDynamoDB _dynamoDbClient;
    private readonly ILogger<TelemetryTableSetup> _logger;

    public TelemetryTableSetup(
        IAmazonDynamoDB dynamoDbClient,
        ILogger<TelemetryTableSetup> logger)
    {
        _dynamoDbClient = dynamoDbClient ?? throw new ArgumentNullException(nameof(dynamoDbClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Creates the telemetry table with proper GSI configuration
    /// </summary>
    public async Task CreateTelemetryTableAsync(
        string tableName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Creating DynamoDB telemetry table: {TableName}", tableName);

            var createTableRequest = new CreateTableRequest
            {
                TableName = tableName,
                BillingMode = BillingMode.PAY_PER_REQUEST,
                
                // Primary key: PartitionKey (String), SortKey (String)
                KeySchema = new List<KeySchemaElement>
                {
                    new KeySchemaElement("PartitionKey", KeyType.HASH),
                    new KeySchemaElement("SortKey", KeyType.RANGE)
                },
                
                AttributeDefinitions = new List<AttributeDefinition>
                {
                    new AttributeDefinition("PartitionKey", ScalarAttributeType.S),
                    new AttributeDefinition("SortKey", ScalarAttributeType.S),
                    new AttributeDefinition("TenantId", ScalarAttributeType.S),
                    new AttributeDefinition("EntryType", ScalarAttributeType.S),
                    new AttributeDefinition("UserId", ScalarAttributeType.S),
                    new AttributeDefinition("Timestamp", ScalarAttributeType.S)
                },
                
                // Global Secondary Indexes for querying patterns
                GlobalSecondaryIndexes = new List<GlobalSecondaryIndex>
                {
                    // GSI1: Query by tenant and entry type
                    new GlobalSecondaryIndex
                    {
                        IndexName = "TenantEntryType-Timestamp-Index",
                        KeySchema = new List<KeySchemaElement>
                        {
                            new KeySchemaElement("TenantId", KeyType.HASH),
                            new KeySchemaElement("Timestamp", KeyType.RANGE)
                        },
                        Projection = new Projection { ProjectionType = ProjectionType.ALL }
                    },
                    
                    // GSI2: Query by tenant and user
                    new GlobalSecondaryIndex
                    {
                        IndexName = "TenantUser-Timestamp-Index",
                        KeySchema = new List<KeySchemaElement>
                        {
                            new KeySchemaElement("TenantId", KeyType.HASH),
                            new KeySchemaElement("UserId", KeyType.RANGE)
                        },
                        Projection = new Projection { ProjectionType = ProjectionType.ALL }
                    },
                    
                    // GSI3: Query by entry type across tenants (for system admin)
                    new GlobalSecondaryIndex
                    {
                        IndexName = "EntryType-Timestamp-Index",
                        KeySchema = new List<KeySchemaElement>
                        {
                            new KeySchemaElement("EntryType", KeyType.HASH),
                            new KeySchemaElement("Timestamp", KeyType.RANGE)
                        },
                        Projection = new Projection { ProjectionType = ProjectionType.ALL }
                    }
                },
                
                
                Tags = new List<Tag>
                {
                    new Tag { Key = "Application", Value = "FhirRag" },
                    new Tag { Key = "Component", Value = "Telemetry" },
                    new Tag { Key = "Environment", Value = "Production" }
                }
            };

            var response = await _dynamoDbClient.CreateTableAsync(createTableRequest, cancellationToken);
            
            _logger.LogInformation("DynamoDB table creation initiated: {TableName}. Status: {Status}",
                tableName, response.TableDescription.TableStatus);

            // Wait for table to become active
            await WaitForTableActiveAsync(tableName, cancellationToken);
            
            // Enable TTL after table creation
            try
            {
                await _dynamoDbClient.UpdateTimeToLiveAsync(new UpdateTimeToLiveRequest
                {
                    TableName = tableName,
                    TimeToLiveSpecification = new TimeToLiveSpecification
                    {
                        AttributeName = "ExpiresAt",
                        Enabled = true
                    }
                }, cancellationToken);
                
                _logger.LogDebug("Enabled TTL for table {TableName}", tableName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to enable TTL for table {TableName}", tableName);
            }
            
            _logger.LogInformation("DynamoDB telemetry table created successfully: {TableName}", tableName);
        }
        catch (ResourceInUseException)
        {
            _logger.LogInformation("DynamoDB table {TableName} already exists", tableName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating DynamoDB telemetry table: {TableName}", tableName);
            throw;
        }
    }

    /// <summary>
    /// Checks if the telemetry table exists
    /// </summary>
    public async Task<bool> TableExistsAsync(string tableName, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _dynamoDbClient.DescribeTableAsync(tableName, cancellationToken);
            return response.Table.TableStatus == TableStatus.ACTIVE;
        }
        catch (ResourceNotFoundException)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if table exists: {TableName}", tableName);
            throw;
        }
    }

    /// <summary>
    /// Deletes the telemetry table (use with caution!)
    /// </summary>
    public async Task DeleteTableAsync(string tableName, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogWarning("Deleting DynamoDB telemetry table: {TableName}", tableName);
            
            await _dynamoDbClient.DeleteTableAsync(tableName, cancellationToken);
            
            _logger.LogWarning("DynamoDB telemetry table deleted: {TableName}", tableName);
        }
        catch (ResourceNotFoundException)
        {
            _logger.LogInformation("DynamoDB table {TableName} does not exist", tableName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting DynamoDB telemetry table: {TableName}", tableName);
            throw;
        }
    }

    /// <summary>
    /// Updates table configuration (e.g., TTL settings)
    /// </summary>
    public async Task UpdateTableConfigurationAsync(
        string tableName,
        int telemetryTtlDays = 90,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Updating DynamoDB table configuration: {TableName}", tableName);

            // Update TTL configuration
            await _dynamoDbClient.UpdateTimeToLiveAsync(new UpdateTimeToLiveRequest
            {
                TableName = tableName,
                TimeToLiveSpecification = new TimeToLiveSpecification
                {
                    AttributeName = "ExpiresAt",
                    Enabled = true
                }
            }, cancellationToken);

            _logger.LogInformation("Updated DynamoDB table configuration: {TableName}", tableName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating DynamoDB table configuration: {TableName}", tableName);
            throw;
        }
    }

    /// <summary>
    /// Gets table statistics and health information
    /// </summary>
    public async Task<TableStatistics> GetTableStatisticsAsync(
        string tableName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _dynamoDbClient.DescribeTableAsync(tableName, cancellationToken);
            var table = response.Table;

            return new TableStatistics
            {
                TableName = tableName,
                Status = table.TableStatus.Value,
                ItemCount = table.ItemCount,
                TableSizeBytes = table.TableSizeBytes,
                ProvisionedThroughput = table.BillingModeSummary?.BillingMode?.Value ?? "PAY_PER_REQUEST",
                GlobalSecondaryIndexCount = table.GlobalSecondaryIndexes?.Count ?? 0,
                CreationDateTime = table.CreationDateTime,
                LastUpdateDateTime = table.LatestStreamLabel != null ? DateTime.UtcNow : table.CreationDateTime
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting table statistics: {TableName}", tableName);
            throw;
        }
    }

    private async Task WaitForTableActiveAsync(string tableName, CancellationToken cancellationToken)
    {
        var maxWaitTime = TimeSpan.FromMinutes(10);
        var pollInterval = TimeSpan.FromSeconds(5);
        var startTime = DateTime.UtcNow;

        while (DateTime.UtcNow - startTime < maxWaitTime)
        {
            try
            {
                var response = await _dynamoDbClient.DescribeTableAsync(tableName, cancellationToken);
                
                if (response.Table.TableStatus == TableStatus.ACTIVE)
                {
                    _logger.LogDebug("Table {TableName} is now active", tableName);
                    return;
                }

                _logger.LogDebug("Waiting for table {TableName} to become active. Current status: {Status}",
                    tableName, response.Table.TableStatus);
                
                await Task.Delay(pollInterval, cancellationToken);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _logger.LogWarning(ex, "Error while waiting for table {TableName} to become active", tableName);
                await Task.Delay(pollInterval, cancellationToken);
            }
        }

        throw new TimeoutException($"Table {tableName} did not become active within {maxWaitTime}");
    }
}

/// <summary>
/// Table statistics for monitoring
/// </summary>
public class TableStatistics
{
    public string TableName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public long ItemCount { get; set; }
    public long TableSizeBytes { get; set; }
    public string ProvisionedThroughput { get; set; } = string.Empty;
    public int GlobalSecondaryIndexCount { get; set; }
    public DateTime CreationDateTime { get; set; }
    public DateTime LastUpdateDateTime { get; set; }

    /// <summary>
    /// Gets the table size in a human-readable format
    /// </summary>
    public string GetFormattedSize()
    {
        if (TableSizeBytes < 1024)
            return $"{TableSizeBytes} bytes";
        
        if (TableSizeBytes < 1024 * 1024)
            return $"{TableSizeBytes / 1024.0:F1} KB";
        
        if (TableSizeBytes < 1024 * 1024 * 1024)
            return $"{TableSizeBytes / (1024.0 * 1024.0):F1} MB";
        
        return $"{TableSizeBytes / (1024.0 * 1024.0 * 1024.0):F1} GB";
    }

    /// <summary>
    /// Estimates monthly cost based on size and usage patterns
    /// </summary>
    public decimal EstimateMonthlyCost(double readRequestsPerSecond = 1.0, double writeRequestsPerSecond = 0.1)
    {
        // Rough estimation for DynamoDB on-demand pricing (US East)
        var storageCostPerGB = 0.25m;
        var readRequestCostPer1M = 0.25m;
        var writeRequestCostPer1M = 1.25m;

        var sizeInGB = (decimal)(TableSizeBytes / (1024.0 * 1024.0 * 1024.0));
        var storageCost = sizeInGB * storageCostPerGB;

        var monthlyReadRequests = (decimal)(readRequestsPerSecond * 60 * 60 * 24 * 30);
        var monthlyWriteRequests = (decimal)(writeRequestsPerSecond * 60 * 60 * 24 * 30);

        var readCost = (monthlyReadRequests / 1_000_000m) * readRequestCostPer1M;
        var writeCost = (monthlyWriteRequests / 1_000_000m) * writeRequestCostPer1M;

        return storageCost + readCost + writeCost;
    }
}