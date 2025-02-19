// See https://aka.ms/new-console-template for more information

using Amazon;
using Amazon.S3;
using BuildThumbnails;
using BuildThumbnails.Services;
using CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

//Build ServiceProvider
ServiceProvider serviceProvider = services.BuildServiceProvider();

//Check if the buckets exists
BucketHandler? bucketHandler = serviceProvider.GetService<BucketHandler>();
ILogger<Program>? logger = serviceProvider.GetService<ILogger<Program>>();
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
    foreach (var s3Object in s3Objects)
    {
        await bucketHandler.CreatePhotoThumbNail(sourceBucket, s3Object.FileName, destinationBucket);
        logger.LogInformation($"Processing file {s3Object.FileName}\t {s3Object.PreassignedUrl}");
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