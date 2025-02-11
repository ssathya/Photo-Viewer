// See https://aka.ms/new-console-template for more information

using Microsoft.Extensions.Configuration;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .Build();

// Read settings from the configuration
IConfigurationSection appSettings = configuration.GetSection("AppSettings");
string applicationName = appSettings["ApplicationName"] ?? "";
string version = appSettings["Version"] ?? "";
int maxItems = int.Parse(appSettings["MaxItems"] ?? "0");

// Display the settings
Console.WriteLine($"Application Name: {applicationName}");
Console.WriteLine($"Version: {version}");
Console.WriteLine($"Max Items: {maxItems}");