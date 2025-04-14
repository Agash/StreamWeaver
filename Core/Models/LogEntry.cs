using Microsoft.Extensions.Logging;

namespace StreamWeaver.Core.Models;

public record LogEntry(DateTime Timestamp, LogLevel Level, string Category, string Message, Exception? Exception);
