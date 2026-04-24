using System.ComponentModel.DataAnnotations;

namespace porganizer.Database;

public class PrdbActorAlias
{
    public Guid Id { get; set; }

    [MaxLength(500)]
    public string Name { get; set; } = string.Empty;

    public Guid? SiteId { get; set; }   // not a FK — referenced site may not be synced

    public Guid ActorId { get; set; }
    public PrdbActor Actor { get; set; } = null!;
}
