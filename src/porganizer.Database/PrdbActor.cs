using System.ComponentModel.DataAnnotations;

namespace porganizer.Database;

public class PrdbActor
{
    public Guid Id { get; set; }

    [MaxLength(500)]
    public string Name { get; set; } = string.Empty;

    public int Gender { get; set; }
    public DateOnly? Birthday { get; set; }
    public int? BirthdayType { get; set; }
    public DateOnly? Deathday { get; set; }

    [MaxLength(200)]
    public string? Birthplace { get; set; }

    public int Haircolor { get; set; }
    public int Eyecolor { get; set; }
    public int BreastType { get; set; }
    public int? Height { get; set; }
    public int? BraSize { get; set; }

    [MaxLength(20)]
    public string? BraSizeLabel { get; set; }

    public int? WaistSize { get; set; }
    public int? HipSize { get; set; }
    public int Nationality { get; set; }
    public int Ethnicity { get; set; }
    public int? CareerStart { get; set; }
    public int? CareerEnd { get; set; }

    [MaxLength(1000)]
    public string? Tattoos { get; set; }

    [MaxLength(1000)]
    public string? Piercings { get; set; }

    public bool IsFavorite { get; set; }
    public DateTime? FavoritedAtUtc { get; set; }

    public DateTime PrdbCreatedAtUtc { get; set; }
    public DateTime PrdbUpdatedAtUtc { get; set; }
    public DateTime SyncedAtUtc { get; set; }
    public DateTime? DetailSyncedAtUtc { get; set; }

    public ICollection<PrdbActorImage> Images { get; set; } = [];
    public ICollection<PrdbActorAlias> Aliases { get; set; } = [];
    public ICollection<PrdbVideoActor> VideoActors { get; set; } = [];
}
