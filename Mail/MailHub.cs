using MailServer.Model;
using Microsoft.AspNetCore.SignalR;

namespace MailServer.Mail;

public interface IMailHubClient
{
	Task ReceiveEmail(ApiEmailGetResponseModel email);
}

public class MailHub : Hub<IMailHubClient>
{
	public async Task SubscribeToEmail(string emailAddress)
	{
		if (string.IsNullOrEmpty(emailAddress))
		{
			return;
		}
		await Groups.AddToGroupAsync(Context.ConnectionId, emailAddress.ToLower());
	}

	public async Task UnsubscribeFromEmail(string emailAddress)
	{
		if (string.IsNullOrEmpty(emailAddress))
		{
			return;
		}
		await Groups.RemoveFromGroupAsync(Context.ConnectionId, emailAddress.ToLower());
	}
}
