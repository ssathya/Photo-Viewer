// See https://aka.ms/new-console-template for more information

using BuildThumbnails.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

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
    .WriteTo.File(logFileName, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 3)
    .CreateLogger();
services.AddLogging(c =>
{
    c.SetMinimumLevel(LogLevel.Information);
    c.AddSerilog(Log.Logger);
});

//Dependency Injection
services.AddTransient<BucketHandler>();

//Build ServiceProvider
ServiceProvider serviceProvider = services.BuildServiceProvider();

//Check if the buckets exists
BucketHandler? bucketHandler = serviceProvider.GetService<BucketHandler>();
if (bucketHandler == null)
{
    Log.Logger.Error("BucketHandler not found");
    Environment.Exit(1);
}

bool bucketExists = await bucketHandler.CheckIfPhotosBucketExists();
if (!bucketExists)
{
    Log.Logger.Error("Bucket not found");
    Environment.Exit(1);
}
bool thumbnailBucketExists = await bucketHandler.CreateThumbnailBucket();
if (!thumbnailBucketExists)
{
    Log.Logger.Error("Thumbnail Bucket could not be created");
    Environment.Exit(1);
}