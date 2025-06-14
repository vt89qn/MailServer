using MailServer.Common;
using MailServer.Mail;
using MailServer.Model;
using Microsoft.Extensions.Caching.Memory;
using System.Collections.Concurrent;
using System.Net;
namespace MailServer.Enpoint;

public static class Email
{
	public static void MapEmail(this WebApplication app)
	{
		var group = app.MapGroup("email");
		group.MapGet("/clear", clear);
		group.MapGet("/", get);
	}
	private static IResult clear(ILogger<WebApplication> logger, IMemoryCache cache, HttpContext context)
	{
		var response = new ApiResponseModel<string>();
		try
		{
			var httpRequest = context.Request;
			var to = httpRequest.Query["to"].ToString();
			if (cache.TryGetValue(CachingConst.Emails, out ConcurrentDictionary<string, ConcurrentBag<MailMessage>> emails) && emails.TryGetValue(to, out var messages))
			{
				messages.Clear();
			}
		}
		catch (Exception ex)
		{
			response.SetHttpStatusCode(HttpStatusCode.InternalServerError);
			logger.LogError(ex, "clear");
		}
		return Results.Ok(response);
	}
	private static IResult get(ILogger<WebApplication> logger, IMemoryCache cache, HttpContext context)
	{
		var response = new ApiResponseModel<List<ApiEmailGetResponseModel>>();
		try
		{
			var httpRequest = context.Request;
			var to = httpRequest.Query["to"].ToString();
			var from = httpRequest.Query["from"].ToString();

			if (cache.TryGetValue(CachingConst.Emails, out ConcurrentDictionary<string, ConcurrentBag<MailMessage>> emails) && emails.TryGetValue(to, out var messages))
			{
				response.Data = [];
				var datas = new List<MailMessage>();
				if (string.IsNullOrEmpty(from))
				{
					datas = messages.ToList();
				}
				else
				{
					datas = messages.Where(x => x.From == from).ToList();
				}
				response.Data = datas.Select(x => new ApiEmailGetResponseModel
				{
					From = x.From,
					To = x.To,
					HtmlBody = x.HtmlBody,
					RecvDate = x.RecvDate,
					TextBody = x.TextBody,
				}).ToList();
			}
		}
		catch (Exception ex)
		{
			response.SetHttpStatusCode(HttpStatusCode.InternalServerError);
			logger.LogError(ex, "get");
		}
		return Results.Ok(response);
	}
}

