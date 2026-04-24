namespace porganizer.Api.Features.Prdb;

public class PrdbActorResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Gender { get; set; }
    public int Nationality { get; set; }
    public DateOnly? Birthday { get; set; }
    public bool IsFavorite { get; set; }
    public DateTime? FavoritedAtUtc { get; set; }
    public List<string> Aliases { get; set; } = [];
    public string? ProfileImageUrl { get; set; }
}
