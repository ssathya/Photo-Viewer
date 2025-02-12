using Amazon;
using Amazon.S3;
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
            //Get the bucket name from the configuration
            var bucketName = configuration["AWSS3:PhotoBucketName"];
            //Check if the bucket exists
            using var s3Client = new AmazonS3Client(RegionEndpoint.GetBySystemName(configuration["AWSS3:PhotoBucketRegion"]));
            try
            {
                var response = await s3Client.ListBucketsAsync();
                return response.Buckets.Any(b => b.BucketName.Equals(bucketName, StringComparison.OrdinalIgnoreCase));
            }
            catch (AmazonS3Exception ex)
            {
                logger.LogError(ex, "Error checking if bucket exists");
            }
            return false;
        }
    }
}