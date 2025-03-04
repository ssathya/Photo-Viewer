namespace Models.Amazon
{
    public class S3ObjectDto
    {
        public string FileName { get; set; } = string.Empty;
        public string PreassignedUrl { get; set; } = string.Empty;
        public DateTime Expiration { get; set; }
        public DateTime LastModifiedDate { get; set; }
    }
}