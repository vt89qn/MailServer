using MailServer.Model;
using Microsoft.AspNetCore.SignalR;
using MimeKit;
using SmtpServer;
using SmtpServer.Protocol;
using SmtpServer.Storage;
using System.Buffers;
using System.Collections.Concurrent;

namespace MailServer.Mail;

public class MailStore(IHubContext<MailHub, IMailHubClient> hubContext, ILogger<MailStore> logger) : IMessageStore, IHostedService
{
	private ConcurrentBag<MailMessage> mailMessages = [];
	private Timer timerCleanup;
	public async Task<SmtpResponse> SaveAsync(ISessionContext context, IMessageTransaction transaction, ReadOnlySequence<byte> buffer, CancellationToken cancellationToken)
	{
		await using var stream = new MemoryStream();

		var position = buffer.GetPosition(0);
		while (buffer.TryGet(ref position, out var memory))
		{
			stream.Write(memory.Span);
		}

		stream.Position = 0;

		var message = await MimeMessage.LoadAsync(stream, cancellationToken);
		var mailMessage = new MailMessage(message);
		mailMessages.Add(mailMessage);


		var tasks = mailMessage.To
		.Select(address =>
			hubContext.Clients.Group(address.ToLower()).ReceiveEmail(new ApiEmailGetResponseModel(mailMessage))
		);
		await Task.WhenAll(tasks);

		logger.LogInformation("{From}->{To} : {Subject}", mailMessage.From, string.Join(", ", mailMessage.To), mailMessage.Subject);

		return SmtpResponse.Ok;
	}

	public IEnumerable<MailMessage> GetMessages(string to, string from)
	{
		var query = mailMessages.AsEnumerable();

		if (!string.IsNullOrEmpty(to))
		{
			query = query.Where(m => m.To.Any(t => t.Equals(to, StringComparison.OrdinalIgnoreCase)));
		}

		if (!string.IsNullOrEmpty(from))
		{
			query = query.Where(m => m.From.Equals(from, StringComparison.OrdinalIgnoreCase));
		}

		return query;
	}

	public void CleanupMessages(object state)
	{
		var maxAge = TimeSpan.FromMinutes(10);

		var keptMessages = new ConcurrentBag<MailMessage>(mailMessages.Where(m => (DateTime.Now - m.RecvDate) <= maxAge));
		var removedCount = mailMessages.Count - keptMessages.Count;

		if (removedCount > 0)
		{
			mailMessages = keptMessages;
		}
	}

	public Task StartAsync(CancellationToken cancellationToken)
	{
		timerCleanup = new Timer(CleanupMessages, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));
		return Task.CompletedTask;

	}

	public Task StopAsync(CancellationToken cancellationToken)
	{
		timerCleanup.Change(Timeout.Infinite, 0);
		timerCleanup.Dispose();
		return Task.CompletedTask;
	}
}
