namespace porganizer.Api.Features.Library;

public class PreviewOptions
{
    /// <summary>
    /// Absolute path to the directory where preview images are stored.
    /// Derived automatically from the database directory at startup ({dataDir}/previews).
    /// </summary>
    public string CachePath { get; set; } = string.Empty;
}
