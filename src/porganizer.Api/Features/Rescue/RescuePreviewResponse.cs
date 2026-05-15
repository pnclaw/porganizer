namespace porganizer.Api.Features.Rescue;

public class RescuePreviewResponse
{
    public List<RescuePreviewItem> Items { get; set; } = [];
}

public class RescuePreviewItem
{
    public string SourcePath { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsMatched { get; set; }
    public string? VideoTitle { get; set; }
    public string? SiteTitle { get; set; }
    public string? DestinationFolder { get; set; }
    public int VideoFileCount { get; set; }
}
