using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BuildThumbnails.Services
{
    public class BucketHandler(IConfiguration configuration, ILogger<BucketHandler> logger)
    {
        private readonly IConfiguration configuration = configuration;
        private readonly ILogger<BucketHandler> logger = logger;

        public async Task<bool> CheckIfPhotosBucketExists()
        {
            //Get the bucket name and region from the configuration
            var bucketName = configuration["AWSS3:PhotoBucketName"] ?? string.Empty;
            RegionEndpoint region = RegionEndpoint.GetBySystemName(configuration["AWSS3:PhotoBucketRegion"]);
            //Check if the bucket exists
            return await DoesS3BucketExist(bucketName, region);
        }

        public async Task<bool> CreateThumbnailBucket()
        {
            //Get the bucket name and region from the configuration
            var bucketName = configuration["AWSS3:ThumbnailBucketName"] ?? string.Empty;
            RegionEndpoint region = RegionEndpoint.GetBySystemName(configuration["AWSS3:ThumbnailBucketRegion"]);
            //Check if the bucket exists
            if (await DoesS3BucketExist(bucketName, region))
            {
                return true;
            }
            return await CreateS3Bucket(bucketName, region);
        }

        private async Task<bool> CreateS3Bucket(string bucketName, RegionEndpoint region)
        {
            using var s3Client = new AmazonS3Client(region);
            try
            {
                var putBucketRequest = new PutBucketRequest
                {
                    BucketName = bucketName,
                    UseClientRegion = true
                };
                var response = await s3Client.PutBucketAsync(putBucketRequest);
                if (response.HttpStatusCode == System.Net.HttpStatusCode.OK)
                {
                    logger.LogInformation($"Bucket {bucketName} created successfully.");
                    return true;
                }
                else
                {
                    logger.LogError($"Error creating bucket {bucketName}: {response.HttpStatusCode}");
                    return false;
                }
            }
            catch (AmazonS3Exception ex)
            {
                logger.LogError(ex, $"Error creating bucket {bucketName}");
                return false;
            }
        }

        private async Task<bool> DoesS3BucketExist(string bucketName, RegionEndpoint region)
        {
            using var s3Client = new AmazonS3Client(region);
            try
            {
                var response = await s3Client.ListBucketsAsync();
                return response.Buckets.Exists(b => b.BucketName == bucketName);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error checking if bucket exists");
                return false;
            }
        }
    }
}