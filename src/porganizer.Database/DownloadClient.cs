using System.ComponentModel.DataAnnotations;
using porganizer.Database.Common;
using porganizer.Database.Enums;

namespace porganizer.Database;

public class DownloadClient : BaseEntity
{
    public Guid Id { get; set; }

    [MaxLength(500)]
    public string Title { get; set; } = string.Empty;

    public ClientType ClientType { get; set; }

    [MaxLength(500)]
    public string Host { get; set; } = string.Empty;

    public int? Port { get; set; }

    public bool UseSsl { get; set; }

    [MaxLength(500)]
    public string ApiKey { get; set; } = string.Empty;

    [MaxLength(500)]
    public string Username { get; set; } = string.Empty;

    [MaxLength(500)]
    public string Password { get; set; } = string.Empty;

    [MaxLength(255)]
    public string Category { get; set; } = string.Empty;

    public bool IsEnabled { get; set; }
}
