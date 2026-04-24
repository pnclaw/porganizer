namespace porganizer.Database;

public class PrdbVideoActor
{
    public Guid VideoId { get; set; }
    public PrdbVideo Video { get; set; } = null!;

    public Guid ActorId { get; set; }
    public PrdbActor Actor { get; set; } = null!;
}
