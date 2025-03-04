// See https://aka.ms/new-console-template for more information

using Amazon;
using Amazon.S3;
using BuildThumbnails;
using BuildThumbnails.Services;
using CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Models;
using Serilog;

DateTime startDate = DateTime.Parse("2025-01-01");
Parser.Default.ParseArguments<Options>(args)
    .WithParsed<Options>(o =>
    {
        if (o != null && o.Date >= DateTime.MinValue)
        {
            startDate = o.Date;
            Log.Logger.Information("Processing date {Date}", o.Date);
        }
    });
IServiceCollection services = new ServiceCollection();
//Configuration
IConfiguration configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .Build();
services.AddSingleton(configuration);
//Logger
string logFileName = Path.Combine(Path.GetTempPath(), "BuildThumbnails-.txt");
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.RollingFile(logFileName, retainedFileCountLimit: 3)
    .CreateLogger();
services.AddLogging(c =>
{
    c.SetMinimumLevel(LogLevel.Information);
    c.AddSerilog(Log.Logger);
});
Log.Logger.Information("Starting BuildThumbnails");
IAmazonS3 s3Client = CreateS3Client(configuration);
//Dependency Injection
services.AddTransient<BucketHandler>();
services.AddSingleton(s3Client);
services.AddTransient<DatabaseHandler>();

//Build ServiceProvider
ServiceProvider serviceProvider = services.BuildServiceProvider();

//Check if the buckets exists
BucketHandler? bucketHandler = serviceProvider.GetService<BucketHandler>();
ILogger<Program>? logger = serviceProvider.GetService<ILogger<Program>>();
DatabaseHandler? databaseHandler = serviceProvider.GetService<DatabaseHandler>();
if (bucketHandler == null)
{
    Log.Logger.Error("BucketHandler not found");
    Environment.Exit(1);
}
if (logger == null)
{
    Log.Logger.Error("Logger not found");
    Environment.Exit(1);
}
if (databaseHandler == null)
{
    Log.Logger.Error("DatabaseHandler not found");
    Environment.Exit(1);
}
bool bucketExists = await bucketHandler.CheckIfPhotosBucketExists();
if (!bucketExists)
{
    logger.LogError("Bucket not found");
    Environment.Exit(1);
}
bool thumbnailBucketExists = await bucketHandler.CreateThumbnailBucket();
if (!thumbnailBucketExists)
{
    logger.LogError("Thumbnail Bucket could not be created");
    Environment.Exit(1);
}
var s3Objects = await bucketHandler.GetFilesFromBucketAfterDate(configuration["AWSS3:PhotoBucketName"] ?? "", startDate);
if (s3Objects != null && s3Objects.Count != 0)
{
    var sourceBucket = configuration["AWSS3:PhotoBucketName"] ?? string.Empty;
    var destinationBucket = configuration["AWSS3:ThumbnailBucketName"] ?? string.Empty;
    if (string.IsNullOrEmpty(sourceBucket) || string.IsNullOrEmpty(destinationBucket))
    {
        logger.LogError("Source or destination bucket not found");
        Environment.Exit(1);
    }
    try
    {
        foreach (var s3Object in s3Objects)
        {
            CreateStatus cs = await bucketHandler.CreatePhotoThumbNail(sourceBucket, s3Object.FileName, destinationBucket);
            if (cs.IsCreationSuccess)
            {
                Image imageDetails = new()
                {
                    ImageFullPath = s3Object.FileName,
                    ThumbnailFullPath = s3Object.FileName,
                    CreationDate = string.IsNullOrEmpty(cs.CreationDate) ? DateTime.MinValue : DateTime.Parse(cs.CreationDate),
                    GPSAltitude = cs.Altitude,
                    GPSLatitude = string.IsNullOrEmpty(cs.GpsLatitude) ? 0 : double.Parse(cs.GpsLatitude),
                    GPSLongitude = string.IsNullOrEmpty(cs.GpsLongitude) ? 0 : double.Parse(cs.GpsLongitude),
                    UploadDate = s3Object.LastModifiedDate
                };
                await databaseHandler.StoreImageDetailsAsync(imageDetails);
            }
        }
    }
    catch (Exception ex)
    {
        Log.Logger.Error(ex, "Error creating thumbnails");
    }
    finally
    {
        databaseHandler.Dispose();
    }
}
Log.CloseAndFlush();

static IAmazonS3 CreateS3Client(IConfiguration configuration)
{
    //Setup S3 Client
    try
    {
        return new AmazonS3Client(RegionEndpoint.GetBySystemName(configuration["AWSS3:ThumbnailBucketRegion"]));
    }
    catch (Exception ex)
    {
        Log.Logger.Error(ex, "Error creating S3 Client");
        Environment.Exit(1);
        return null;
    }
}