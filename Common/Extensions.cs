using MailServer.Common;
using Microsoft.Extensions.Caching.Memory;
using Serilog;
using Serilog.Events;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

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
public static class StringExtension
{
	public static string ToMD5(this string input)
	{
		byte[] data = MD5.HashData(Encoding.UTF8.GetBytes(input));
		StringBuilder sBuilder = new();
		for (int i = 0; i < data.Length; i++)
		{
			sBuilder.Append(data[i].ToString("x2"));
		}
		return sBuilder.ToString();
	}
	public static string ToSHA256(this string input)
	{
		byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
		StringBuilder builder = new();
		for (int i = 0; i < bytes.Length; i++)
		{
			builder.Append(bytes[i].ToString("x2"));
		}
		return builder.ToString();
	}
	public static string ToHMACSHA256(this string input, string secret)
	{
		byte[] hashBytes = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(input));
		StringBuilder builder = new();
		for (int i = 0; i < hashBytes.Length; i++)
		{
			builder.Append(hashBytes[i].ToString("x2"));
		}
		return builder.ToString();
	}
}
public static class CacheExtension
{
	public static TItem GetOrCreate<TItem>(this IMemoryCache cache, string key, TItem item)
	{
		return cache.GetOrCreate(key, entry => { entry.Priority = CacheItemPriority.NeverRemove; return item; });
	}
	public static TItem GetOrCreate<TItem>(this IMemoryCache cache, string key, TItem item, TimeSpan slidingExpiration)
	{
		return cache.GetOrCreate(key, entry => { entry.SlidingExpiration = slidingExpiration; return item; });
	}
}
public static class DateTimeExtensions
{
	public static string ToDateTimeString(this DateTime dateTime, string format = "yyyy-MM-dd HH:mm:ss")
	{
		return dateTime.ToString(format, CultureInfo.GetCultureInfo("en-US"));
	}
	public static string ToDateString(this DateTime dateTime, string format = "yyyy-MM-dd")
	{
		return dateTime.ToDateTimeString(format);
	}
	public static string ToTimeString(this DateTime dateTime, string format = "HH:mm:ss")
	{
		return dateTime.ToDateTimeString(format);
	}
	public static int ToInt(this DateTime dateTime)
	{
		return int.Parse(dateTime.ToDateTimeString("yyMMdd"));
	}
	public static DateTime ToDate(this int iDateTime)
	{
		DateTime.TryParseExact(iDateTime.ToString(), "yyMMdd", CultureInfo.GetCultureInfo("en-US"), DateTimeStyles.None, out var date);
		return date;
	}
	public static bool ToDate(this string dateString, out DateTime outDate, string format = "yyyy-MM-dd")
	{
		return DateTime.TryParseExact(dateString, format, CultureInfo.GetCultureInfo("en-US"), DateTimeStyles.None, out outDate);
	}
}
