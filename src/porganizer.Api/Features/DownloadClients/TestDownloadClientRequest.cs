using System.ComponentModel.DataAnnotations;
using porganizer.Database.Enums;

namespace porganizer.Api.Features.DownloadClients;

public class TestDownloadClientRequest
{
    [Required]
    public ClientType ClientType { get; set; }

    [Required]
    [MaxLength(500)]
    public string Host { get; set; } = string.Empty;

    [Required]
    public int Port { get; set; }

    public bool UseSsl { get; set; }

    [MaxLength(500)]
    public string ApiKey { get; set; } = string.Empty;

    [MaxLength(500)]
    public string Username { get; set; } = string.Empty;

    [MaxLength(500)]
    public string Password { get; set; } = string.Empty;
}
