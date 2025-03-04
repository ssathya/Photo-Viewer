using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Models;
using Models.Amazon;
using SixLabors.ImageSharp.Processing;
using System.Text.RegularExpressions;

namespace BuildThumbnails.Services
{
    public class BucketHandler(IConfiguration configuration, IAmazonS3 s3Client, ILogger<BucketHandler> logger)
    {
        private readonly IConfiguration configuration = configuration;
        private readonly ILogger<BucketHandler> logger = logger;
        private readonly IAmazonS3 s3Client = s3Client;

        public async Task<bool> CheckIfPhotosBucketExists()
        {
            //Get the bucket name and region from the configuration
            var bucketName = configuration["AWSS3:PhotoBucketName"] ?? string.Empty;
            RegionEndpoint region = RegionEndpoint.GetBySystemName(configuration["AWSS3:PhotoBucketRegion"]);
            //Check if the bucket exists
            return await DoesS3BucketExist(bucketName, region);
        }

        public async Task<CreateStatus> CreatePhotoThumbNail(string sourceBucket, string key, string destinationBucket)
        {
            string tmpFile = string.Empty;
            try
            {
                if (key.Contains(".mp4"))
                {
                    logger.LogInformation($"Thumbnail for {key}");
                }

                var request2 = new ListObjectsV2Request
                {
                    BucketName = destinationBucket,
                    Prefix = key,
                    MaxKeys = 1
                };
                var response2 = await s3Client.ListObjectsV2Async(request2);
                if (response2.S3Objects.Count > 0)
                {
                    logger.LogInformation($"Thumbnail for {key} already exists in {destinationBucket}");
                    return new CreateStatus
                    {
                        IsCreationSuccess = false,
                        DestinationBucket = destinationBucket,
                        DestinationKey = key
                    };
                }
                tmpFile = await DownloadObjectAsync(sourceBucket, key);
                IReadOnlyList<MetadataExtractor.Directory> metadata = ImageMetadataReader.ReadMetadata(tmpFile);

                string? location = string.Empty;
                string? altitude = string.Empty;
                string? creationDate = string.Empty;
                string? gpsLatitude = string.Empty;
                string? gpsLongitude = string.Empty;
                creationDate = ExtractGPSInfo(key, metadata, ref location, ref altitude, ref gpsLatitude, ref gpsLongitude);
                if (!key.Contains(".mp4", StringComparison.OrdinalIgnoreCase))
                {
                    using var image = SixLabors.ImageSharp.Image.Load(tmpFile);
                    image.Mutate(x => x.Resize(100, 100));
                    using var outputStream = new MemoryStream();
                    image.Save(outputStream, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder());
                    outputStream.Position = 0;
                    string imageType = "image/jpeg";
                    await SaveThumbnailToBucket(key, destinationBucket, outputStream, imageType);
                }
                else
                {
                    var appPath = AppDomain.CurrentDomain.BaseDirectory;
                    var picturePath = Path.Combine(appPath, "Assets/Movie.png");
                    FileStream outputSteam = File.OpenRead(picturePath);
                    string imageType = "image/png";
                    await SaveThumbnailToBucket(key, destinationBucket, outputSteam, imageType);
                }
                return new CreateStatus
                {
                    IsCreationSuccess = true,
                    DestinationBucket = destinationBucket,
                    DestinationKey = key,
                    Altitude = altitude,
                    Location = location,
                    CreationDate = creationDate,
                    GpsLatitude = gpsLatitude,
                    GpsLongitude = gpsLongitude
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
            finally
            {
                if (!string.IsNullOrEmpty(tmpFile))
                {
                    File.Delete(tmpFile);
                }
            }
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

        public async Task<List<S3ObjectDto>> GetFilesFromBucketAfterDate(string bucketName, DateTime date)
        {
            try
            {
                var request = new ListObjectsV2Request
                {
                    BucketName = bucketName,
                    MaxKeys = 1000
                };
                List<S3ObjectDto> s3Objects = [];
                ListObjectsV2Response response;
                do
                {
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
                                LastModifiedDate = entry.LastModified,
                                PreassignedUrl = s3Client.GetPreSignedURL(urlRequest),
                                Expiration = DateTime.Now.AddMinutes(5)
                            };
                            s3Objects.Add(s3ObjectDto);
                        }
                    }
                    request.ContinuationToken = response.NextContinuationToken;
                } while (response.IsTruncated);

                return s3Objects;
            }
            catch (AmazonS3Exception ex)
            {
                logger.LogError(ex, $"Error getting files from bucket {bucketName}");
                return [];
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

        private async Task<string> DownloadObjectAsync(string sourceBucket, string key)
        {
            using var response = await s3Client.GetObjectAsync(sourceBucket, key);
            if (response.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
            {
                throw new AmazonS3Exception($"Object {key} not found in bucket {sourceBucket}");
            }
            using Stream responseStream = response.ResponseStream;
            var extension = Path.GetExtension(key);
            string tempFile = Path.GetTempPath() + Guid.NewGuid().ToString() + extension;
            using FileStream fileStream = new(tempFile, FileMode.Create, FileAccess.Write);
            await responseStream.CopyToAsync(fileStream);
            return tempFile;
        }

        private string? ExtractGPSInfo(string key,
            IReadOnlyList<MetadataExtractor.Directory> metadata,
            ref string? location,
            ref string? altitude,
            ref string gpsLatitude,
            ref string gpsLongitude)
        {
            string? dateTime = string.Empty;
            GpsDirectory? gpsDirectory = metadata.OfType<GpsDirectory>().FirstOrDefault();
            if (gpsDirectory != null)
            {
                altitude = gpsDirectory.GetDescription(GpsDirectory.TagAltitude);

                dateTime = gpsDirectory.GetDescription(GpsDirectory.TagDateTime);
                if (string.IsNullOrEmpty(dateTime))
                {
                    dateTime = metadata.SelectMany(d => d.Tags).Where(t => t.Name == "Date/Time Original").FirstOrDefault()?.Description;
                }
                GeoLocation? geoLocation = gpsDirectory.GetGeoLocation();
                if (geoLocation != null)
                {
                    gpsLatitude = geoLocation.Latitude.ToString();
                    gpsLongitude = geoLocation.Longitude.ToString();
                }
                else
                {
                    location = metadata.SelectMany(d => d.Tags).Where(t => t.Name == "Subject Location").FirstOrDefault()?.Description;
                    logger.LogInformation($"No GPS data found for file {key}");
                }
            }
            else
            {
                logger.LogInformation($"No GPS data found for file {key}");
            }
            if (!string.IsNullOrEmpty(dateTime))
            {
                int count = dateTime.Count(c => c == ':');
                if (count >= 4)
                {
                    string pattern = @"(\d{4}):(\d{2}):(\d{2}) (\d{2}:\d{2}:\d{2})";
                    string replacement = "$1-$2-$3 $4";
                    dateTime = Regex.Replace(dateTime, pattern, replacement);
                }
            }
            return dateTime;
        }

        private async Task SaveThumbnailToBucket(string key, string destinationBucket, Stream outputStream, string imageType)
        {
            var putObjectRequest = new PutObjectRequest
            {
                BucketName = destinationBucket,
                Key = key,
                InputStream = outputStream,
                ContentType = imageType
            };
            await s3Client.PutObjectAsync(putObjectRequest);
        }
    }
}