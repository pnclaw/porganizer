namespace porganizer.Api.Features.Rescue;

public class RescueExecuteResponse
{
    public List<RescueExecuteItem> Items { get; set; } = [];
}

public class RescueExecuteItem
{
    public string SourcePath { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsMatched { get; set; }
    public List<RescueLogEntry> Log { get; set; } = [];
}

public class RescueLogEntry
{
    public string Level { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
