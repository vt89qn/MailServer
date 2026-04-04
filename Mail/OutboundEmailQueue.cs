using MailServer.Common;
using MailServer.Model;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace MailServer.Mail;

public class OutboundQueueOptions
{
	public const string SectionName = "OutboundQueue";

	public int MaxAttempts { get; set; } = 8;
	public int PollIntervalMilliseconds { get; set; } = 1000;
	public int InitialRetryDelaySeconds { get; set; } = 30;
	public int MaxRetryDelayMinutes { get; set; } = 30;
}

public class OutboundEmailQueue(EmailSender emailSender, IOptions<OutboundQueueOptions> options, ILogger<OutboundEmailQueue> logger) : BackgroundService
{
	private readonly OutboundQueueOptions queueOptions = options.Value;
	private readonly ConcurrentDictionary<string, OutboundEmailQueueItem> items = [];
	private readonly SemaphoreSlim signal = new(0);

	public Task<ApiEmailSendResponseModel> EnqueueAsync(ApiEmailSendRequestModel request, CancellationToken cancellationToken)
	{
		var prepared = emailSender.Prepare(request);
		var now = DateTime.Now;
		var item = new OutboundEmailQueueItem
		{
			Id = Guid.NewGuid().ToString("N"),
			Message = prepared,
			Status = "Queued",
			QueuedDate = now,
			NextAttemptDate = now
		};

		items[item.Id] = item;
		signal.Release();
		return Task.FromResult(CreateResponse(item));
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		while (!stoppingToken.IsCancellationRequested)
		{
			await ProcessDueItemsAsync(stoppingToken);

			var delay = GetNextDelay();
			try
			{
				await signal.WaitAsync(delay, stoppingToken);
			}
			catch (OperationCanceledException)
			{
				break;
			}
		}
	}

	private async Task ProcessDueItemsAsync(CancellationToken cancellationToken)
	{
		var now = DateTime.Now;
		var dueItems = items.Values
			.Where(x => x.IsActive && !x.IsProcessing && x.NextAttemptDate <= now)
			.OrderBy(x => x.NextAttemptDate)
			.ToList();

		foreach (var item in dueItems)
		{
			if (!item.TryStart())
			{
				continue;
			}

			try
			{
				item.AttemptCount++;
				item.Status = item.AttemptCount == 1 ? "Sending" : "Retrying";
				item.LastError = null;

				await emailSender.DeliverAsync(item.Message, cancellationToken);

				item.Status = "Sent";
				item.SentDate = DateTime.Now;
				item.NextAttemptDate = item.SentDate;
				logger.LogInformation("Outbound email {Id} sent after {AttemptCount} attempt(s)", item.Id, item.AttemptCount);
			}
			catch (EmailDeliveryException ex) when (ex.IsTransient && item.AttemptCount < queueOptions.MaxAttempts)
			{
				item.Status = "RetryScheduled";
				item.LastError = ex.Message;
				item.NextAttemptDate = DateTime.Now.Add(GetRetryDelay(item.AttemptCount));
				logger.LogWarning(ex, "Outbound email {Id} will retry at {NextAttempt}", item.Id, item.NextAttemptDate!.Value.ToDateTimeString());
				signal.Release();
			}
			catch (Exception ex)
			{
				item.Status = "Failed";
				item.LastError = ex.Message;
				item.NextAttemptDate = null;
				logger.LogError(ex, "Outbound email {Id} failed permanently after {AttemptCount} attempt(s)", item.Id, item.AttemptCount);
			}
			finally
			{
				item.IsProcessing = false;
			}
		}
	}

	private TimeSpan GetNextDelay()
	{
		var now = DateTime.Now;
		var nextAttempt = items.Values
			.Where(x => x.IsActive && !x.IsProcessing)
			.Select(x => x.NextAttemptDate)
			.Where(x => x.HasValue)
			.OrderBy(x => x.Value)
			.FirstOrDefault();

		if (!nextAttempt.HasValue)
		{
			return TimeSpan.FromMilliseconds(queueOptions.PollIntervalMilliseconds);
		}

		var delay = nextAttempt.Value - now;
		if (delay <= TimeSpan.Zero)
		{
			return TimeSpan.Zero;
		}

		var maxDelay = TimeSpan.FromMilliseconds(queueOptions.PollIntervalMilliseconds);
		return delay < maxDelay ? delay : maxDelay;
	}

	private TimeSpan GetRetryDelay(int attemptCount)
	{
		var seconds = queueOptions.InitialRetryDelaySeconds * Math.Pow(2, Math.Max(0, attemptCount - 1));
		var maxDelay = TimeSpan.FromMinutes(queueOptions.MaxRetryDelayMinutes);
		var delay = TimeSpan.FromSeconds(seconds);
		return delay < maxDelay ? delay : maxDelay;
	}

	private static ApiEmailSendResponseModel CreateResponse(OutboundEmailQueueItem item)
	{
		return new ApiEmailSendResponseModel
		{
			Id = item.Id,
			From = item.Message.From,
			To = [.. item.Message.To],
			Cc = [.. item.Message.Cc],
			Bcc = [.. item.Message.Bcc],
			Subject = item.Message.Subject,
			Status = item.Status,
			QueuedDate = item.QueuedDate.ToDateTimeString(),
			NextAttemptDate = item.NextAttemptDate?.ToDateTimeString(),
			AttemptCount = item.AttemptCount,
			LastError = item.LastError,
			SentDate = item.SentDate?.ToDateTimeString()
		};
	}
}

sealed class OutboundEmailQueueItem
{
	public string Id { get; set; }
	public PreparedEmailMessage Message { get; set; }
	public string Status { get; set; }
	public DateTime QueuedDate { get; set; }
	public DateTime? NextAttemptDate { get; set; }
	public DateTime? SentDate { get; set; }
	public int AttemptCount { get; set; }
	public string LastError { get; set; }
	public bool IsProcessing { get; set; }

	public bool IsActive => !string.Equals(Status, "Sent", StringComparison.OrdinalIgnoreCase)
		&& !string.Equals(Status, "Failed", StringComparison.OrdinalIgnoreCase);

	public bool TryStart()
	{
		if (IsProcessing || !IsActive)
		{
			return false;
		}

		IsProcessing = true;
		return true;
	}
}
