namespace porganizer.Api.Features.DownloadClients;

public enum MoveLogLevel { Info = 0, Warning = 1, Error = 2 }

public record MoveLogEntry(MoveLogLevel Level, string Message);
