using MailServer.Common;
using MailServer.Mail;
using MailServer.Model;
using System.Net;
namespace MailServer.Enpoint;

static class Email
{
	public static void MapEmail(this WebApplication app)
	{
		var group = app.MapGroup("email");
		group.MapGet("/", get);
	}
	private static IResult get(MailStore mailStore, ILogger<WebApplication> logger, HttpContext context)
	{
		var response = new ApiResponseModel<List<ApiEmailGetResponseModel>>();
		try
		{
			var httpRequest = context.Request;
			var to = httpRequest.Query["to"].ToString();
			var from = httpRequest.Query["from"].ToString();

			var messages = mailStore.GetMessages(to, from);

			response.Data = messages.Select(x => new ApiEmailGetResponseModel
			{
				Subject = x.MimeMessage.Subject,
				From = x.From,
				To = x.To,
				HtmlBody = x.MimeMessage.HtmlBody,
				RecvDate = x.RecvDate.ToDateTimeString(),
				TextBody = x.MimeMessage.TextBody,
			}).ToList();
		}
		catch (Exception ex)
		{
			response.SetHttpStatusCode(HttpStatusCode.InternalServerError);
			logger.LogError(ex, "get");
		}
		return Results.Ok(response);
	}
}

