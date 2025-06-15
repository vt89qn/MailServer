using Serilog;
using Serilog.Events;
using System.Globalization;

namespace MailServer.Common;

static class HostBuilderExtensions
{
	public static IHostBuilder AddLog(this IHostBuilder builder, string logPath)
	{
		return builder.UseSerilog((context, configuration) =>
			configuration.ReadFrom.Configuration(context.Configuration).Enrich.FromLogContext()
			.MinimumLevel.Debug().MinimumLevel.Override("Microsoft", LogEventLevel.Warning).MinimumLevel.Override("System", LogEventLevel.Warning)

			.WriteTo.Logger(lc => lc.Filter.ByIncludingOnly($"@l = 'Information' and SourceContext <> 'Serilog.AspNetCore.RequestLoggingMiddleware'")
					.WriteTo.File(path: Path.Combine(logPath, "info-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 24
						, outputTemplate: "{Timestamp:HHmmss.fff}	{Message:lj}{NewLine}"))


			.WriteTo.Logger(lc => lc.Filter.ByIncludingOnly($"@l in ['Error', 'Warning']")
							.WriteTo.File(path: Path.Combine(logPath, "error-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 24
								, outputTemplate: "{Timestamp:HHmmss.fff}	{Message:lj}{NewLine}{Exception}"))
			);
	}
}

public static class DateTimeExtensions
{
	public static string ToDateTimeString(this DateTime dateTime, string format = "yyyy-MM-dd HH:mm:ss")
	{
		return dateTime.ToString(format, CultureInfo.GetCultureInfo("en-US"));
	}
}
