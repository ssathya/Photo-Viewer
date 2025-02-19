namespace Models
{
    public class CreateStatus
    {
        public bool IsCreationSuccess { get; set; }
        public string DestinationBucket { get; set; } = string.Empty;
        public string DestinationKey { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
    }
}