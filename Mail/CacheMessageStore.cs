using MailServer.Common;
using Microsoft.Extensions.Caching.Memory;
using MimeKit;
using SmtpServer;
using SmtpServer.Protocol;
using SmtpServer.Storage;
using System.Buffers;
using System.Collections.Concurrent;

namespace MailServer.Mail;

public class CacheMessageStore(IMemoryCache cache, ILogger<CacheMessageStore> logger) : IMessageStore
{
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
		var toAddress = string.Empty;
		var fromAddress = string.Empty;
		if (message.To.FirstOrDefault() is MailboxAddress to)
		{
			toAddress = to.Address;
		}

		if (message.From.FirstOrDefault() is MailboxAddress from)
		{
			fromAddress = from.Address;
		}
		if (!string.IsNullOrEmpty(fromAddress) && !string.IsNullOrEmpty(toAddress))
		{
			var emails = cache.GetOrCreate(CachingConst.Emails, new ConcurrentDictionary<string, ConcurrentBag<MailMessage>>());
			if (!emails.TryGetValue(toAddress, out var email))
			{
				email = [];
				emails.TryAdd(toAddress, email);
			}
			email.Add(new MailMessage(message));
			logger.LogInformation($"{fromAddress}->{toAddress} : {message.Subject}////{message.Body}");
		}
		return SmtpResponse.Ok;
	}
}
