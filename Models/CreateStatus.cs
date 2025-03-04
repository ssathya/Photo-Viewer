namespace Models
{
    public class CreateStatus
    {
        public bool IsCreationSuccess { get; set; }
        public string DestinationBucket { get; set; } = string.Empty;
        public string DestinationKey { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public string? Altitude { get; set; } = string.Empty;
        public string? Location { get; set; } = string.Empty;
        public string? GpsLatitude { get; set; } = string.Empty;
        public string? GpsLongitude { get; set; } = string.Empty;
        public string? CreationDate { get; set; } = string.Empty;
    }
}