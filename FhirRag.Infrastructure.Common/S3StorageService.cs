using Amazon.S3;
using Amazon.S3.Model;
using FhirRag.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace FhirRag.Infrastructure.Common;

/// <summary>
/// S3 implementation of the storage service interface
/// </summary>
public class S3StorageService : IStorageService
{
    private readonly IAmazonS3 _s3Client;
    private readonly ILogger<S3StorageService> _logger;
    private readonly S3StorageConfiguration _configuration;

    public S3StorageService(
        IAmazonS3 s3Client,
        ILogger<S3StorageService> logger,
        IOptions<S3StorageConfiguration> configuration)
    {
        _s3Client = s3Client;
        _logger = logger;
        _configuration = configuration.Value;
    }

    public async Task<bool> StoreAsync(string key, byte[] data, Dictionary<string, string>? metadata = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new PutObjectRequest
            {
                BucketName = _configuration.BucketName,
                Key = key,
                InputStream = new MemoryStream(data),
                ContentType = "application/octet-stream",
                ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256
            };

            // Add metadata if provided
            if (metadata != null)
            {
                foreach (var kvp in metadata)
                {
                    request.Metadata[kvp.Key] = kvp.Value;
                }
            }

            var response = await _s3Client.PutObjectAsync(request, cancellationToken);
            
            _logger.LogDebug("Successfully stored object {Key} in bucket {BucketName}", key, _configuration.BucketName);
            return response.HttpStatusCode == System.Net.HttpStatusCode.OK;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store object {Key} in bucket {BucketName}", key, _configuration.BucketName);
            return false;
        }
    }

    public async Task<byte[]?> RetrieveAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new GetObjectRequest
            {
                BucketName = _configuration.BucketName,
                Key = key
            };

            using var response = await _s3Client.GetObjectAsync(request, cancellationToken);
            using var memoryStream = new MemoryStream();
            
            await response.ResponseStream.CopyToAsync(memoryStream, cancellationToken);
            var data = memoryStream.ToArray();
            
            _logger.LogDebug("Successfully retrieved object {Key} from bucket {BucketName}", key, _configuration.BucketName);
            
            return data;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogDebug("Object {Key} not found in bucket {BucketName}", key, _configuration.BucketName);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve object {Key} from bucket {BucketName}", key, _configuration.BucketName);
            return null;
        }
    }

    public async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new DeleteObjectRequest
            {
                BucketName = _configuration.BucketName,
                Key = key
            };

            var response = await _s3Client.DeleteObjectAsync(request, cancellationToken);
            
            _logger.LogDebug("Successfully deleted object {Key} from bucket {BucketName}", key, _configuration.BucketName);
            return response.HttpStatusCode == System.Net.HttpStatusCode.NoContent;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete object {Key} from bucket {BucketName}", key, _configuration.BucketName);
            return false;
        }
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new GetObjectMetadataRequest
            {
                BucketName = _configuration.BucketName,
                Key = key
            };

            await _s3Client.GetObjectMetadataAsync(request, cancellationToken);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check existence of object {Key} in bucket {BucketName}", key, _configuration.BucketName);
            return false;
        }
    }

    public async Task<IEnumerable<string>> ListKeysAsync(string? prefix = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new ListObjectsV2Request
            {
                BucketName = _configuration.BucketName,
                Prefix = prefix ?? string.Empty,
                MaxKeys = 1000
            };

            var keys = new List<string>();
            ListObjectsV2Response response;

            do
            {
                response = await _s3Client.ListObjectsV2Async(request, cancellationToken);
                keys.AddRange(response.S3Objects.Select(obj => obj.Key));
                request.ContinuationToken = response.NextContinuationToken;
            }
            while (response.IsTruncated == true);

            _logger.LogDebug("Listed {Count} objects with prefix {Prefix} from bucket {BucketName}", keys.Count, prefix, _configuration.BucketName);
            return keys;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list objects with prefix {Prefix} from bucket {BucketName}", prefix, _configuration.BucketName);
            return Enumerable.Empty<string>();
        }
    }

    public Task<string?> GeneratePresignedUrlAsync(string key, TimeSpan expiration, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new GetPreSignedUrlRequest
            {
                BucketName = _configuration.BucketName,
                Key = key,
                Expires = DateTime.UtcNow.Add(expiration),
                Verb = HttpVerb.GET
            };

            var url = _s3Client.GetPreSignedURL(request);
            _logger.LogDebug("Generated presigned URL for object {Key} in bucket {BucketName}", key, _configuration.BucketName);
            
            return Task.FromResult<string?>(url);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate presigned URL for object {Key} in bucket {BucketName}", key, _configuration.BucketName);
            return Task.FromResult<string?>(null);
        }
    }
}

/// <summary>
/// Configuration for S3 storage service
/// </summary>
public class S3StorageConfiguration
{
    public string BucketName { get; set; } = string.Empty;
    public string Region { get; set; } = "us-east-1";
    public bool UseServerSideEncryption { get; set; } = true;
    public int MaxRetryAttempts { get; set; } = 3;
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromMinutes(5);
}