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
		group.MapPost("/", Post);
		group.MapGet("/test", TestSend);

		group.MapHub<MailHub>("/hub");
	}
	private static async Task<IResult> TestSend(OutboundEmailQueue outboundEmailQueue, ILogger<WebApplication> logger, CancellationToken cancellationToken)
	{
		try
		{
			var email = await outboundEmailQueue.EnqueueAsync(new ApiEmailSendRequestModel { From = "noreply@gsm.vin", To = ["vantin.work@gmail.com"], TextBody = "Your authentication code\r\n\r\nPlease use the following code to help verify your identity:\r\n\r\n085300\r\n\r\nBest,\r\ngsm.vin", Subject = "Your authentication code" }, cancellationToken);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "TestSend");
		}
		return Results.Ok("Ok");
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

	private static async Task<IResult> Post(ApiEmailSendRequestModel request, OutboundEmailQueue outboundEmailQueue, ILogger<WebApplication> logger, CancellationToken cancellationToken)
	{
		var response = new ApiResponseModel<ApiEmailSendResponseModel>();
		try
		{
			response.Data = await outboundEmailQueue.EnqueueAsync(request, cancellationToken);
		}
		catch (ArgumentException ex)
		{
			response.SetHttpStatusCode(HttpStatusCode.BadRequest);
			response.Msg = ex.Message;
			logger.LogWarning(ex, "send validation failed");
		}
		catch (InvalidOperationException ex)
		{
			response.SetHttpStatusCode(HttpStatusCode.InternalServerError);
			response.Msg = ex.Message;
			logger.LogError(ex, "send configuration invalid");
		}
		catch (Exception ex)
		{
			response.SetHttpStatusCode(HttpStatusCode.InternalServerError);
			logger.LogError(ex, "send");
		}
		return Results.Ok(response);
	}
}
