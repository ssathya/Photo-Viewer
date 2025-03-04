using Amazon.S3;
using Amazon.S3.Model;
using LiteDB.Async;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Models;

namespace BuildThumbnails.Services
{
    public class DatabaseHandler(IConfiguration configuration, IAmazonS3 s3Client, ILogger<DatabaseHandler> logger) : IDisposable
    {
        private const string filePathInStorage = "Database/photos.db";
        private static string? databasePath = null;
        private readonly IConfiguration configuration = configuration;
        private readonly ILogger<DatabaseHandler> logger = logger;
        private readonly IAmazonS3 s3Client = s3Client;
        private LiteDatabaseAsync? db;

        ~DatabaseHandler()
        {
            Dispose();
        }

        public void Dispose()
        {
            var bucketName = configuration["AWSS3:ThumbnailBucketName"] ?? string.Empty;
            if (!string.IsNullOrEmpty(databasePath) && File.Exists(databasePath))
            {
                db?.Dispose();
                try
                {
                    s3Client.PutObjectAsync(new PutObjectRequest
                    {
                        BucketName = bucketName,
                        Key = filePathInStorage,
                        FilePath = databasePath
                    }).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    logger.LogCritical($"Error uploading file to S3: {ex.Message}");
                }
                File.Delete(databasePath);
            }
        }

        public async Task<bool> StoreImageDetailsAsync(Image imageDetails)
        {
            var db = await GetDatabase();
            var imageCollection = db.GetCollection<Image>();
            try
            {
                if (await imageCollection.ExistsAsync(i => i.ImageFullPath == imageDetails.ImageFullPath))
                {
                    //Update Metadata here later

                    return true;
                }
                else
                {
                    await imageCollection.InsertAsync(imageDetails);
                }
                return true;
            }
            catch (Exception ex)
            {
                logger.LogError($"Error storing image details: {ex.Message}");
                return false;
            }
        }

        private static string GenerateLocalFileName()
        {
            if (!string.IsNullOrEmpty(databasePath))
            {
                return databasePath;
            }
            string tempFilePath = Path.GetTempPath();
            databasePath = Path.Combine(tempFilePath, $"{Guid.NewGuid()}.db");
            return databasePath;
        }

        private async Task CreateAndDownloadFileAsync(string bucketName)
        {
            try
            {
                var putRequest = new PutObjectRequest
                {
                    BucketName = bucketName,
                    Key = filePathInStorage,
                    InputStream = new MemoryStream()
                };
                await s3Client.PutObjectAsync(putRequest);
            }
            catch (Exception)
            {
                logger.LogCritical($"Error Creating file in S3: {databasePath}");
            }
            await DownloadFileAsync(bucketName);
        }

        private async Task DownloadFileAsync(string bucketName)
        {
            try
            {
                var request = new GetObjectRequest
                {
                    BucketName = bucketName,
                    Key = filePathInStorage
                };
                using var response = await s3Client.GetObjectAsync(request);
                using var responseStream = response.ResponseStream;
                using var fileStream = new FileStream(GenerateLocalFileName(), FileMode.Create, FileAccess.Write);
                await responseStream.CopyToAsync(fileStream);
            }
            catch (Exception ex)
            {
                logger.LogCritical($"Error downloading file from S3: {ex.Message}");
            }
        }

        private async Task<bool> FileExistsAsync()
        {
            var bucketName = configuration["AWSS3:ThumbnailBucketName"] ?? string.Empty;
            try
            {
                var request = new GetObjectMetadataRequest
                {
                    BucketName = bucketName,
                    Key = filePathInStorage
                };

                var response = await s3Client.GetObjectMetadataAsync(request);
                return response.HttpStatusCode == System.Net.HttpStatusCode.OK;
            }
            catch (AmazonS3Exception ex)
            {
                logger.LogCritical($"Error checking file existence in S3: {ex.Message}");
                return false;
            }
        }

        private async Task<LiteDatabaseAsync> GetDatabase()
        {
            if (db != null)
            {
                return db;
            }
            var bucketName = configuration["AWSS3:ThumbnailBucketName"] ?? string.Empty;
            GenerateLocalFileName();
            if (await FileExistsAsync())
            {
                await DownloadFileAsync(bucketName);
            }
            else
            {
                await CreateAndDownloadFileAsync(bucketName);
            }
            db = new LiteDatabaseAsync(GenerateLocalFileName());
            return db;
        }
    }
}