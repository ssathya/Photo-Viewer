namespace Models;

public class Image
{
    public int Id { get; set; }
    public string ImageFullPath { get; set; } = string.Empty;
    public DateTime UploadDate { get; set; }
    public DateTime CreationDate { get; set; }

    public string Description { get; set; } = string.Empty;
    public string ThumbnailFullPath { get; set; } = string.Empty;
    public List<Tag> Tags { get; set; } = [];
    public string? GPSLatitudeRef { get; set; }
    public double? GPSLatitude { get; set; }
    public string? GPSLongitudeRef { get; set; }
    public double? GPSLongitude { get; set; }
    public string? GPSAltitudeRef { get; set; }
    public string? GPSAltitude { get; set; }
    public string? GPSDateTime { get; set; }
}