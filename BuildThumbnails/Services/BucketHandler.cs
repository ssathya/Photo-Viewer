using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Models;
using Models.Amazon;
using SixLabors.ImageSharp.Processing;

namespace BuildThumbnails.Services
{
    public class BucketHandler(IConfiguration configuration, IAmazonS3 s3Client, ILogger<BucketHandler> logger)
    {
        private readonly IConfiguration configuration = configuration;
        private readonly IAmazonS3 s3Client = s3Client;
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

        public async Task<CreateStatus> CreatePhotoThumbNail(string sourceBucket, string key, string destinationBucket)
        {
            try
            {
                using GetObjectResponse response1 = await s3Client.GetObjectAsync(destinationBucket, key);
                if (response1.HttpStatusCode == System.Net.HttpStatusCode.OK && response1.ContentLength > 0)
                {
                    logger.LogInformation($"Thumbnail for {key} already exists in {destinationBucket}");
                    return new CreateStatus
                    {
                        IsCreationSuccess = true,
                        DestinationBucket = destinationBucket,
                        DestinationKey = key
                    };
                }
                using GetObjectResponse response = await s3Client.GetObjectAsync(sourceBucket, key);
                using var responseStream = response.ResponseStream;
                using var image = SixLabors.ImageSharp.Image.Load(responseStream);
                image.Mutate(x => x.Resize(100, 100));
                using var outputStream = new MemoryStream();
                image.Save(outputStream, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder());
                outputStream.Position = 0;
                var putObjectRequest = new PutObjectRequest
                {
                    BucketName = destinationBucket,
                    Key = key,
                    InputStream = outputStream,
                    ContentType = "image/jpeg"
                };
                await s3Client.PutObjectAsync(putObjectRequest);
                return new CreateStatus
                {
                    IsCreationSuccess = true,
                    DestinationBucket = destinationBucket,
                    DestinationKey = key
                };
            }
            catch (AmazonS3Exception ex)
            {
                logger.LogError(ex, $"Error creating thumbnail for {key}");
                return new CreateStatus
                {
                    IsCreationSuccess = false,
                    DestinationBucket = destinationBucket,
                    DestinationKey = key,
                    ErrorMessage = ex.Message
                };
            }
        }

        private async Task<bool> CreateS3Bucket(string bucketName, RegionEndpoint region)
        {
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

        public async Task<List<S3ObjectDto>> GetFilesFromBucketAfterDate(string bucketName, DateTime date)
        {
            try
            {
                var request = new ListObjectsV2Request
                {
                    BucketName = bucketName,
                    MaxKeys = 200
                };
                List<S3ObjectDto> s3Objects = [];
                ListObjectsV2Response response;
                response = await s3Client.ListObjectsV2Async(request);
                foreach (S3Object entry in response.S3Objects)
                {
                    if (entry.Key.EndsWith("/"))
                    {
                        continue;
                    }
                    if (entry.LastModified > date)
                    {
                        GetPreSignedUrlRequest urlRequest = new GetPreSignedUrlRequest
                        {
                            BucketName = bucketName,
                            Key = entry.Key,
                            Expires = DateTime.Now.AddMinutes(5)
                        };
                        S3ObjectDto s3ObjectDto = new S3ObjectDto
                        {
                            FileName = entry.Key,
                            CreationDate = entry.LastModified,
                            PreassignedUrl = s3Client.GetPreSignedURL(urlRequest),
                            Expiration = DateTime.Now.AddMinutes(5)
                        };
                        s3Objects.Add(s3ObjectDto);
                    }
                }
                return s3Objects;
            }
            catch (AmazonS3Exception ex)
            {
                logger.LogError(ex, $"Error getting files from bucket {bucketName}");
                return [];
            }
        }

        private async Task<bool> DoesS3BucketExist(string bucketName, RegionEndpoint region)
        {
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