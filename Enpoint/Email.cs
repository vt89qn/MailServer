using MailServer.Mail;
using MailServer.Model;
using System.Net;
namespace MailServer.Enpoint;

static class Email
{
	public static void MapEmail(this WebApplication app)
	{
		var group = app.MapGroup("email");
		group.MapGet("/", Get);

		group.MapHub<MailHub>("/hub");

	}
	private static IResult Get(MailStore mailStore, ILogger<WebApplication> logger, HttpContext context)
	{
		var response = new ApiResponseModel<List<ApiEmailGetResponseModel>>();
		try
		{
			var httpRequest = context.Request;
			var to = httpRequest.Query["to"].ToString();
			var from = httpRequest.Query["from"].ToString();

			var messages = mailStore.GetMessages(to, from);

			response.Data = [.. messages.Select(x => new ApiEmailGetResponseModel(x))];
		}
		catch (Exception ex)
		{
			response.SetHttpStatusCode(HttpStatusCode.InternalServerError);
			logger.LogError(ex, "get");
		}
		return Results.Ok(response);
	}
}

