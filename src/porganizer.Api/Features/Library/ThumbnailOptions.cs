namespace porganizer.Api.Features.Library;

public class ThumbnailOptions
{
    /// <summary>
    /// Absolute path to the directory where sprite sheet thumbnails are stored.
    /// Derived automatically from the database directory at startup ({dataDir}/thumbnails).
    /// </summary>
    public string CachePath { get; set; } = string.Empty;
}
