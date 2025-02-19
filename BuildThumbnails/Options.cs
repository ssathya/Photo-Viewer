using CommandLine;

namespace BuildThumbnails;

public class Options
{
    [Option('d', "date", Required = false, HelpText = "The date to process")]
    public DateTime Date { get; set; } = DateTime.Now;
}