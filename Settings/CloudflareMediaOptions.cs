namespace PTfinder.API.Settings;

public sealed class CloudflareMediaOptions
{
    public const string SectionName = "CloudflareMedia";

    public bool Enabled { get; set; }
    public string AccountId { get; set; } = string.Empty;
    public string ApiToken { get; set; } = string.Empty;
    public string StreamCustomerCode { get; set; } = string.Empty;
    public string ImagesDeliveryHash { get; set; } = string.Empty;
    public string ImageVariant { get; set; } = "feed";
    public string ImageThumbnailVariant { get; set; } = "thumbnail";
    public int MaxVideoDurationSeconds { get; set; } = 180;
    public long MaxVideoBytes { get; set; } = 200_000_000;
    public long MaxImageBytes { get; set; } = 10_000_000;
}
