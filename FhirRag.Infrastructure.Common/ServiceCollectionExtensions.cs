using Amazon.DynamoDBv2;
using Amazon.OpenSearchService;
using Amazon.S3;
using FhirRag.Core.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FhirRag.Infrastructure.Common;

/// <summary>
/// Extension methods for registering infrastructure services
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds infrastructure services to the service collection
    /// </summary>
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Configure AWS services
        services.AddAWSService<IAmazonS3>();
        services.AddAWSService<IAmazonDynamoDB>();
        services.AddAWSService<IAmazonOpenSearchService>();

        // Configure service options
        services.Configure<S3StorageConfiguration>(configuration.GetSection("AWS:S3"));
        services.Configure<DynamoDbConfiguration>(configuration.GetSection("AWS:DynamoDB"));
        services.Configure<OpenSearchConfiguration>(configuration.GetSection("AWS:OpenSearch"));

        // Register services
        services.AddScoped<IStorageService, S3StorageService>();
        services.AddScoped<DynamoDbService>();
        services.AddScoped<OpenSearchService>();

        // Register HTTP client for OpenSearch
        services.AddHttpClient<OpenSearchService>((serviceProvider, client) =>
        {
            var config = serviceProvider.GetRequiredService<IOptions<OpenSearchConfiguration>>().Value;
            if (!string.IsNullOrEmpty(config.Username) && !string.IsNullOrEmpty(config.Password))
            {
                var credentials = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($"{config.Username}:{config.Password}"));
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
            }
            client.Timeout = config.DefaultTimeout;
        });

        return services;
    }

    /// <summary>
    /// Adds infrastructure services with custom AWS configuration
    /// </summary>
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<Amazon.Extensions.NETCore.Setup.AWSOptions>? awsOptionsAction = null)
    {
        // Configure AWS services with custom options
        if (awsOptionsAction != null)
        {
            var awsOptions = new Amazon.Extensions.NETCore.Setup.AWSOptions();
            awsOptionsAction(awsOptions);
            services.AddDefaultAWSOptions(awsOptions);
        }

        services.AddAWSService<IAmazonS3>();
        services.AddAWSService<IAmazonDynamoDB>();
        services.AddAWSService<IAmazonOpenSearchService>();

        // Configure service options
        services.Configure<S3StorageConfiguration>(configuration.GetSection("AWS:S3"));
        services.Configure<DynamoDbConfiguration>(configuration.GetSection("AWS:DynamoDB"));
        services.Configure<OpenSearchConfiguration>(configuration.GetSection("AWS:OpenSearch"));

        // Register services
        services.AddScoped<IStorageService, S3StorageService>();
        services.AddScoped<DynamoDbService>();
        services.AddScoped<OpenSearchService>();

        // Register HTTP client for OpenSearch
        services.AddHttpClient<OpenSearchService>((serviceProvider, client) =>
        {
            var config = serviceProvider.GetRequiredService<IOptions<OpenSearchConfiguration>>().Value;
            if (!string.IsNullOrEmpty(config.Username) && !string.IsNullOrEmpty(config.Password))
            {
                var credentials = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($"{config.Username}:{config.Password}"));
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
            }
            client.Timeout = config.DefaultTimeout;
        });

        return services;
    }
}

/// <summary>
/// AWS service configuration helper
/// </summary>
public static class AwsServiceConfiguration
{
    /// <summary>
    /// Creates AWS options for local development (LocalStack)
    /// </summary>
    public static Amazon.Extensions.NETCore.Setup.AWSOptions CreateLocalStackOptions()
    {
        return new Amazon.Extensions.NETCore.Setup.AWSOptions
        {
            DefaultClientConfig =
            {
                ServiceURL = "http://localhost:4566",
                UseHttp = true,
                AuthenticationRegion = "us-east-1"
            }
        };
    }

    /// <summary>
    /// Creates AWS options for specific region
    /// </summary>
    public static Amazon.Extensions.NETCore.Setup.AWSOptions CreateRegionOptions(string region)
    {
        return new Amazon.Extensions.NETCore.Setup.AWSOptions
        {
            Region = Amazon.RegionEndpoint.GetBySystemName(region)
        };
    }
}