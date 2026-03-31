using Microsoft.Extensions.Logging;

namespace Logging;

public static class LoggingBuilderExtensions
{
    public static ILoggingBuilder AddWebShopLogging(this ILoggingBuilder logging)
    {
        logging.ClearProviders();
        logging.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "HH:mm:ss ";
            options.UseUtcTimestamp = true;
        });

        logging.SetMinimumLevel(LogLevel.Information);
        return logging;
    }
}

