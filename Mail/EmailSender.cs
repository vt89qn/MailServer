using MailServer.Common;
using MailServer.Model;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Cryptography;
using MimeKit.Utils;
using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;

namespace MailServer.Mail;

public class SmtpSenderOptions
{
	public const string SectionName = "SmtpSender";

	public string LocalDomain { get; set; }
	public int Port { get; set; } = 25;
	public bool EnableStartTls { get; set; } = true;
	public bool PreferIPv4 { get; set; } = true;
	public bool AllowIPv6 { get; set; } = true;
	public int ConnectTimeoutMilliseconds { get; set; } = 10000;
	public int CommandTimeoutMilliseconds { get; set; } = 30000;
	public List<string> NameServers { get; set; } = [];
}

public class DkimOptions
{
	public const string SectionName = "Dkim";

	public string Domain { get; set; }
	public string Selector { get; set; }
	public string PrivateKeyPath { get; set; }
	public string AgentOrUserIdentifier { get; set; }
}

public class EmailDeliveryException(string message, bool isTransient, string smtpReplyCode = null, Exception innerException = null) : Exception(message, innerException)
{
	public bool IsTransient { get; } = isTransient;
	public string SmtpReplyCode { get; } = smtpReplyCode;
}

public class PreparedEmailMessage
{
	public PreparedEmailMessage()
	{
		To = [];
		Cc = [];
		Bcc = [];
	}

	public string From { get; set; }
	public List<string> To { get; set; }
	public List<string> Cc { get; set; }
	public List<string> Bcc { get; set; }
	public string Subject { get; set; }
	public string TextBody { get; set; }
	public string HtmlBody { get; set; }

	public IReadOnlyList<string> AllRecipients =>
		[.. To.Concat(Cc).Concat(Bcc).Distinct(StringComparer.OrdinalIgnoreCase)];
}

public class EmailSender(IOptions<SmtpSenderOptions> smtpOptions, IOptions<DkimOptions> dkimOptions, ILogger<EmailSender> logger)
{
	private readonly SmtpSenderOptions smtpSenderOptions = smtpOptions.Value;
	private readonly DkimOptions dkimSenderOptions = dkimOptions.Value;

	public PreparedEmailMessage Prepare(ApiEmailSendRequestModel request)
	{
		Validate(request);

		return new PreparedEmailMessage
		{
			From = NormalizeAddress(request.From),
			To = [.. request.To.Select(NormalizeAddress)],
			Cc = [.. request.Cc.Select(NormalizeAddress)],
			Bcc = [.. request.Bcc.Select(NormalizeAddress)],
			Subject = request.Subject,
			TextBody = request.TextBody,
			HtmlBody = request.HtmlBody
		};
	}

	public async Task DeliverAsync(PreparedEmailMessage message, CancellationToken cancellationToken)
	{
		var errors = new List<EmailDeliveryException>();
		var recipients = message.AllRecipients;

		foreach (var recipientGroup in recipients.GroupBy(GetDomain, StringComparer.OrdinalIgnoreCase))
		{
			try
			{
				await SendToRecipientDomainAsync(message, recipientGroup.Key, [.. recipientGroup], cancellationToken);
			}
			catch (EmailDeliveryException ex)
			{
				logger.LogError(ex, "Direct SMTP delivery failed for domain {Domain}", recipientGroup.Key);
				errors.Add(ex);
			}
			catch (Exception ex)
			{
				var wrapped = new EmailDeliveryException($"Unexpected delivery failure for {recipientGroup.Key}: {ex.Message}", true, null, ex);
				logger.LogError(ex, "Unexpected direct SMTP delivery failure for domain {Domain}", recipientGroup.Key);
				errors.Add(wrapped);
			}
		}

		if (errors.Count > 0)
		{
			var isTransient = errors.All(x => x.IsTransient);
			throw new EmailDeliveryException(string.Join(" | ", errors.Select(x => x.Message)), isTransient, errors.Select(x => x.SmtpReplyCode).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)), errors[^1]);
		}

		logger.LogInformation("Sent email {From}->{To} : {Subject}", message.From, string.Join(", ", recipients), message.Subject);
	}

	private async Task SendToRecipientDomainAsync(PreparedEmailMessage message, string recipientDomain, List<string> recipients, CancellationToken cancellationToken)
	{
		var resolver = new DnsMxResolver(smtpSenderOptions);
		var mailHosts = await resolver.ResolveMailHostsAsync(recipientDomain, cancellationToken);
		var errors = new List<EmailDeliveryException>();

		foreach (var host in mailHosts)
		{
			try
			{
				using var connection = await DirectSmtpConnection.ConnectAsync(host, smtpSenderOptions, logger, cancellationToken);
				var mimeMessage = BuildMessage(message);
				await connection.SendMessageAsync(mimeMessage, message.From, recipients, cancellationToken);
				await connection.QuitAsync(cancellationToken);
				return;
			}
			catch (EmailDeliveryException ex)
			{
				errors.Add(ex);
				logger.LogWarning(ex, "Delivery attempt to host {Host} failed for domain {Domain}", host, recipientDomain);
			}
			catch (Exception ex)
			{
				var wrapped = new EmailDeliveryException($"Connection to {host} failed: {ex.Message}", true, null, ex);
				errors.Add(wrapped);
				logger.LogWarning(ex, "Delivery attempt to host {Host} crashed for domain {Domain}", host, recipientDomain);
			}
		}

		var isTransient = errors.Count > 0 && errors.All(x => x.IsTransient);
		throw new EmailDeliveryException($"Could not deliver to {recipientDomain}", isTransient, errors.Select(x => x.SmtpReplyCode).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)), errors.LastOrDefault());
	}

	private MimeMessage BuildMessage(PreparedEmailMessage request)
	{
		var localDomain = GetLocalDomain();
		var message = new MimeMessage
		{
			Subject = request.Subject ?? string.Empty,
			Date = DateTimeOffset.Now,
			MessageId = MimeUtils.GenerateMessageId(localDomain)
		};

		message.From.Add(MailboxAddress.Parse(request.From));

		foreach (var address in request.To)
		{
			message.To.Add(MailboxAddress.Parse(address));
		}

		foreach (var address in request.Cc)
		{
			message.Cc.Add(MailboxAddress.Parse(address));
		}

		var bodyBuilder = new BodyBuilder
		{
			TextBody = request.TextBody,
			HtmlBody = request.HtmlBody
		};

		message.Body = bodyBuilder.ToMessageBody();
		ApplyDkimSignature(message);
		return message;
	}

	private void ApplyDkimSignature(MimeMessage message)
	{
		if (string.IsNullOrWhiteSpace(dkimSenderOptions.PrivateKeyPath))
		{
			return;
		}

		if (string.IsNullOrWhiteSpace(dkimSenderOptions.Domain))
		{
			throw new InvalidOperationException($"Missing configuration: {DkimOptions.SectionName}:Domain");
		}

		if (string.IsNullOrWhiteSpace(dkimSenderOptions.Selector))
		{
			throw new InvalidOperationException($"Missing configuration: {DkimOptions.SectionName}:Selector");
		}
		var keyPath = Path.Combine(SystemConst.KeysPath, dkimSenderOptions.PrivateKeyPath);
		if (!File.Exists(keyPath))
		{
			throw new InvalidOperationException($"DKIM private key file not found: {dkimSenderOptions.PrivateKeyPath}");
		}

		using var stream = File.OpenRead(keyPath);
		var signer = new DkimSigner(stream, dkimSenderOptions.Domain, dkimSenderOptions.Selector, DkimSignatureAlgorithm.RsaSha256)
		{
			HeaderCanonicalizationAlgorithm = DkimCanonicalizationAlgorithm.Relaxed,
			BodyCanonicalizationAlgorithm = DkimCanonicalizationAlgorithm.Relaxed,
			AgentOrUserIdentifier = string.IsNullOrWhiteSpace(dkimSenderOptions.AgentOrUserIdentifier) ? $"@{dkimSenderOptions.Domain}" : dkimSenderOptions.AgentOrUserIdentifier
		};

		message.Prepare(EncodingConstraint.SevenBit, 78);
		var headers = new List<HeaderId>
		{
			HeaderId.From,
			HeaderId.To,
			HeaderId.Cc,
			HeaderId.Subject,
			HeaderId.Date,
			HeaderId.MessageId,
			HeaderId.MimeVersion,
			HeaderId.ContentType,
			HeaderId.ContentTransferEncoding
		};
		signer.Sign(message, headers);
	}

	private string GetLocalDomain()
	{
		if (!string.IsNullOrWhiteSpace(smtpSenderOptions.LocalDomain))
		{
			return smtpSenderOptions.LocalDomain.Trim();
		}

		var hostName = Dns.GetHostName();
		if (string.IsNullOrWhiteSpace(hostName))
		{
			throw new InvalidOperationException($"Missing configuration: {SmtpSenderOptions.SectionName}:LocalDomain");
		}

		return hostName;
	}

	private static string GetDomain(string emailAddress)
	{
		var atIndex = emailAddress.LastIndexOf('@');
		if (atIndex <= 0 || atIndex == emailAddress.Length - 1)
		{
			throw new ArgumentException($"Invalid recipient address: {emailAddress}");
		}

		return emailAddress[(atIndex + 1)..];
	}

	private static string NormalizeAddress(string emailAddress)
	{
		if (string.IsNullOrWhiteSpace(emailAddress))
		{
			throw new ArgumentException("Email address is required.");
		}

		try
		{
			return MailboxAddress.Parse(emailAddress.Trim()).Address;
		}
		catch (Exception ex)
		{
			throw new ArgumentException($"Invalid email address: {emailAddress}", ex);
		}
	}

	private static void Validate(ApiEmailSendRequestModel request)
	{
		if (request is null)
		{
			throw new ArgumentException("Request body is required.");
		}

		if (string.IsNullOrWhiteSpace(request.From))
		{
			throw new ArgumentException("From is required.");
		}

		if (request.To is null || request.To.Count == 0 || request.To.All(string.IsNullOrWhiteSpace))
		{
			throw new ArgumentException("At least one recipient is required.");
		}

		if (string.IsNullOrWhiteSpace(request.TextBody) && string.IsNullOrWhiteSpace(request.HtmlBody))
		{
			throw new ArgumentException("TextBody or HtmlBody is required.");
		}
	}
}

file sealed class DirectSmtpConnection : IDisposable
{
	private readonly TcpClient tcpClient;
	private Stream stream;
	private readonly string remoteHost;
	private readonly string localDomain;
	private readonly int commandTimeoutMilliseconds;
	private readonly bool enableStartTls;
	private readonly ILogger logger;

	private DirectSmtpConnection(TcpClient tcpClient, Stream stream, string remoteHost, string localDomain, int commandTimeoutMilliseconds, bool enableStartTls, ILogger logger)
	{
		this.tcpClient = tcpClient;
		this.stream = stream;
		this.remoteHost = remoteHost;
		this.localDomain = localDomain;
		this.commandTimeoutMilliseconds = commandTimeoutMilliseconds;
		this.enableStartTls = enableStartTls;
		this.logger = logger;
	}

	public static async Task<DirectSmtpConnection> ConnectAsync(string remoteHost, SmtpSenderOptions options, ILogger logger, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(remoteHost))
		{
			throw new ArgumentException("Remote host is required.");
		}

		try
		{
			using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			connectTimeout.CancelAfter(options.ConnectTimeoutMilliseconds);

			var targetAddresses = await ResolveTargetAddressesAsync(remoteHost, options, connectTimeout.Token);
			Exception lastConnectError = null;
			TcpClient tcpClient = null;

			foreach (var targetAddress in targetAddresses)
			{
				try
				{
					tcpClient?.Dispose();
					tcpClient = new TcpClient(targetAddress.AddressFamily);
					await tcpClient.ConnectAsync(targetAddress, options.Port, connectTimeout.Token);
					logger.LogDebug("Connected to {RemoteHost} using {TargetAddress}", remoteHost, targetAddress);
					break;
				}
				catch (Exception ex)
				{
					lastConnectError = ex;
					logger.LogWarning(ex, "Failed connecting to {RemoteHost} via {TargetAddress}", remoteHost, targetAddress);
					tcpClient?.Dispose();
					tcpClient = null;
				}
			}

			if (tcpClient is null)
			{
				throw new EmailDeliveryException($"Failed to connect to {remoteHost}: {lastConnectError?.Message ?? "No reachable IP address"}", true, null, lastConnectError);
			}

			Stream stream = tcpClient.GetStream();
			var connection = new DirectSmtpConnection(
				tcpClient,
				stream,
				remoteHost,
				string.IsNullOrWhiteSpace(options.LocalDomain) ? Dns.GetHostName() : options.LocalDomain.Trim(),
				options.CommandTimeoutMilliseconds,
				options.EnableStartTls,
				logger);

			var greeting = await connection.ReadResponseAsync(cancellationToken);
			connection.EnsureReply(greeting, ["220"], "server greeting");

			var ehlo = await connection.SendCommandAsync($"EHLO {connection.localDomain}", cancellationToken);
			connection.EnsureReply(ehlo, ["250"], "EHLO");

			if (connection.enableStartTls && ehlo.Lines.Any(line => line.Contains("STARTTLS", StringComparison.OrdinalIgnoreCase)))
			{
				var startTls = await connection.SendCommandAsync("STARTTLS", cancellationToken);
				connection.EnsureReply(startTls, ["220"], "STARTTLS");

				var sslStream = new SslStream(connection.stream, false);
				await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
				{
					TargetHost = remoteHost
				}, cancellationToken);
				connection.stream = sslStream;

				var tlsEhlo = await connection.SendCommandAsync($"EHLO {connection.localDomain}", cancellationToken);
				connection.EnsureReply(tlsEhlo, ["250"], "EHLO after STARTTLS");
			}

			return connection;
		}
		catch (EmailDeliveryException)
		{
			throw;
		}
		catch (Exception ex)
		{
			throw new EmailDeliveryException($"Failed to connect to {remoteHost}: {ex.Message}", true, null, ex);
		}
	}

	private static async Task<IReadOnlyList<IPAddress>> ResolveTargetAddressesAsync(string remoteHost, SmtpSenderOptions options, CancellationToken cancellationToken)
	{
		var addresses = await Dns.GetHostAddressesAsync(remoteHost, cancellationToken);
		var filtered = addresses
			.Where(address => address.AddressFamily == AddressFamily.InterNetwork || (options.AllowIPv6 && address.AddressFamily == AddressFamily.InterNetworkV6))
			.OrderBy(address => options.PreferIPv4 && address.AddressFamily == AddressFamily.InterNetwork ? 0 : 1)
			.ToList();

		if (filtered.Count == 0)
		{
			throw new EmailDeliveryException($"No supported IP address found for {remoteHost}", true);
		}

		return filtered;
	}

	public async Task SendMessageAsync(MimeMessage message, string fromAddress, List<string> recipients, CancellationToken cancellationToken)
	{
		var mailFrom = await SendCommandAsync($"MAIL FROM:<{fromAddress}>", cancellationToken);
		EnsureReply(mailFrom, ["250"], "MAIL FROM");

		foreach (var recipient in recipients)
		{
			var rcptTo = await SendCommandAsync($"RCPT TO:<{recipient}>", cancellationToken);
			EnsureReply(rcptTo, ["250", "251", "252"], $"RCPT TO {recipient}");
		}

		var data = await SendCommandAsync("DATA", cancellationToken);
		EnsureReply(data, ["354"], "DATA");

		await WriteMessageDataAsync(message, cancellationToken);

		var completed = await ReadResponseAsync(cancellationToken);
		EnsureReply(completed, ["250"], "message body");
	}

	public async Task QuitAsync(CancellationToken cancellationToken)
	{
		try
		{
			await SendCommandAsync("QUIT", cancellationToken);
		}
		catch (Exception ex)
		{
			logger.LogDebug(ex, "QUIT failed for {RemoteHost}", remoteHost);
		}
	}

	private async Task<SmtpReply> SendCommandAsync(string command, CancellationToken cancellationToken)
	{
		var payload = Encoding.ASCII.GetBytes(command + "\r\n");
		await stream.WriteAsync(payload, cancellationToken);
		await stream.FlushAsync(cancellationToken);
		return await ReadResponseAsync(cancellationToken);
	}

	private async Task WriteMessageDataAsync(MimeMessage message, CancellationToken cancellationToken)
	{
		using var memoryStream = new MemoryStream();
		await message.WriteToAsync(memoryStream, cancellationToken);

		var rawMessage = Encoding.UTF8.GetString(memoryStream.ToArray())
			.Replace("\r\n", "\n", StringComparison.Ordinal)
			.Replace("\r", "\n", StringComparison.Ordinal);

		var builder = new StringBuilder();
		foreach (var line in rawMessage.Split('\n'))
		{
			if (line.StartsWith(".", StringComparison.Ordinal))
			{
				builder.Append('.');
			}

			builder.Append(line);
			builder.Append("\r\n");
		}

		builder.Append(".\r\n");
		var payload = Encoding.UTF8.GetBytes(builder.ToString());
		await stream.WriteAsync(payload, cancellationToken);
		await stream.FlushAsync(cancellationToken);
	}

	private async Task<SmtpReply> ReadResponseAsync(CancellationToken cancellationToken)
	{
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(commandTimeoutMilliseconds);

		var lines = new List<string>();
		while (true)
		{
			var line = await ReadLineAsync(timeout.Token);
			if (line.Length < 3)
			{
				throw new EmailDeliveryException($"Invalid SMTP response from {remoteHost}: {line}", true);
			}

			lines.Add(line);
			if (line.Length == 3 || line[3] != '-')
			{
				return new SmtpReply(line[..3], lines);
			}
		}
	}

	private async Task<string> ReadLineAsync(CancellationToken cancellationToken)
	{
		var bytes = new List<byte>();
		var buffer = new byte[1];

		while (true)
		{
			var read = await stream.ReadAsync(buffer, cancellationToken);
			if (read == 0)
			{
				throw new EmailDeliveryException($"Connection closed by {remoteHost}", true);
			}

			var value = buffer[0];
			if (value == '\n')
			{
				break;
			}

			if (value != '\r')
			{
				bytes.Add(value);
			}
		}

		return Encoding.ASCII.GetString([.. bytes]);
	}

	private void EnsureReply(SmtpReply reply, IReadOnlyCollection<string> acceptedCodes, string commandName)
	{
		if (!acceptedCodes.Contains(reply.Code, StringComparer.Ordinal))
		{
			var isTransient = reply.Code.Length > 0 && reply.Code[0] == '4';
			throw new EmailDeliveryException($"{commandName} failed on {remoteHost}: {string.Join(" | ", reply.Lines)}", isTransient, reply.Code);
		}
	}

	public void Dispose()
	{
		stream.Dispose();
		tcpClient.Dispose();
	}
}

file sealed class SmtpReply(string code, List<string> lines)
{
	public string Code { get; } = code;
	public List<string> Lines { get; } = lines;
}

file sealed class DnsMxResolver(SmtpSenderOptions options)
{
	private const ushort RecordTypeMx = 15;

	public async Task<IReadOnlyList<string>> ResolveMailHostsAsync(string domain, CancellationToken cancellationToken)
	{
		var nameServers = GetNameServers();
		Exception lastError = null;

		foreach (var nameServer in nameServers)
		{
			try
			{
				var records = await QueryMxRecordsAsync(nameServer, domain, cancellationToken);
				if (records.Count > 0)
				{
					return [.. records.OrderBy(x => x.Preference).Select(x => x.Exchange).Distinct(StringComparer.OrdinalIgnoreCase)];
				}
			}
			catch (Exception ex)
			{
				lastError = ex;
			}
		}

		if (lastError is not null && nameServers.Count == 0)
		{
			throw new InvalidOperationException("No DNS server configured for MX lookup.", lastError);
		}

		return [domain];
	}

	private List<IPAddress> GetNameServers()
	{
		var configured = options.NameServers
			.Where(value => !string.IsNullOrWhiteSpace(value))
			.Select(IPAddress.Parse)
			.ToList();

		if (configured.Count > 0)
		{
			return configured;
		}

		return NetworkInterface.GetAllNetworkInterfaces()
			.Where(nic => nic.OperationalStatus == OperationalStatus.Up)
			.SelectMany(nic => nic.GetIPProperties().DnsAddresses)
			.Where(address => address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
			.Distinct()
			.ToList();
	}

	private static async Task<List<MxRecord>> QueryMxRecordsAsync(IPAddress nameServer, string domain, CancellationToken cancellationToken)
	{
		var query = BuildDnsQuery(domain, RecordTypeMx);
		using var udpClient = new UdpClient(nameServer.AddressFamily);
		udpClient.Connect(new IPEndPoint(nameServer, 53));
		await udpClient.SendAsync(query, cancellationToken);
		var response = await udpClient.ReceiveAsync(cancellationToken);
		return ParseMxRecords(response.Buffer);
	}

	private static byte[] BuildDnsQuery(string domain, ushort recordType)
	{
		var random = Random.Shared.Next(0, ushort.MaxValue + 1);
		var packet = new List<byte>(512);

		packet.Add((byte)(random >> 8));
		packet.Add((byte)random);
		packet.Add(0x01);
		packet.Add(0x00);
		packet.Add(0x00);
		packet.Add(0x01);
		packet.Add(0x00);
		packet.Add(0x00);
		packet.Add(0x00);
		packet.Add(0x00);
		packet.Add(0x00);
		packet.Add(0x00);

		foreach (var label in domain.Split('.', StringSplitOptions.RemoveEmptyEntries))
		{
			var bytes = Encoding.ASCII.GetBytes(label);
			packet.Add((byte)bytes.Length);
			packet.AddRange(bytes);
		}

		packet.Add(0x00);
		packet.Add((byte)(recordType >> 8));
		packet.Add((byte)recordType);
		packet.Add(0x00);
		packet.Add(0x01);

		return [.. packet];
	}

	private static List<MxRecord> ParseMxRecords(byte[] message)
	{
		if (message.Length < 12)
		{
			return [];
		}

		var questionCount = BinaryPrimitives.ReadUInt16BigEndian(message.AsSpan(4, 2));
		var answerCount = BinaryPrimitives.ReadUInt16BigEndian(message.AsSpan(6, 2));
		var offset = 12;

		for (var i = 0; i < questionCount; i++)
		{
			_ = ReadDomainName(message, ref offset);
			offset += 4;
		}

		var records = new List<MxRecord>();
		for (var i = 0; i < answerCount; i++)
		{
			_ = ReadDomainName(message, ref offset);

			var recordType = BinaryPrimitives.ReadUInt16BigEndian(message.AsSpan(offset, 2));
			offset += 2;

			var recordClass = BinaryPrimitives.ReadUInt16BigEndian(message.AsSpan(offset, 2));
			offset += 2;

			offset += 4;

			var dataLength = BinaryPrimitives.ReadUInt16BigEndian(message.AsSpan(offset, 2));
			offset += 2;

			if (recordType == RecordTypeMx && recordClass == 1)
			{
				var preference = BinaryPrimitives.ReadUInt16BigEndian(message.AsSpan(offset, 2));
				var exchangeOffset = offset + 2;
				var exchange = ReadDomainName(message, ref exchangeOffset);
				records.Add(new MxRecord(preference, exchange.TrimEnd('.')));
			}

			offset += dataLength;
		}

		return records;
	}

	private static string ReadDomainName(byte[] message, ref int offset)
	{
		var labels = new List<string>();
		var position = offset;
		var jumped = false;
		var safeGuard = 0;

		while (position < message.Length && safeGuard++ < 256)
		{
			var length = message[position];
			if (length == 0)
			{
				position++;
				break;
			}

			if ((length & 0xC0) == 0xC0)
			{
				var pointer = ((length & 0x3F) << 8) | message[position + 1];
				if (!jumped)
				{
					offset = position + 2;
				}

				position = pointer;
				jumped = true;
				continue;
			}

			position++;
			labels.Add(Encoding.ASCII.GetString(message, position, length));
			position += length;
		}

		if (!jumped)
		{
			offset = position;
		}

		return string.Join(".", labels);
	}
}

file sealed class MxRecord(ushort preference, string exchange)
{
	public ushort Preference { get; } = preference;
	public string Exchange { get; } = exchange;
}
