using SmtpServer;

namespace MailServer.Mail;

public class MailService(IServiceProvider serviceProvider, ILogger<MailService> logger) : IHostedService
{
	private SmtpServer.SmtpServer smtpServer;
	public Task StartAsync(CancellationToken cancellationToken)
	{
		var options = new SmtpServerOptionsBuilder()
			.ServerName("localhost")
			.Endpoint(builder => builder.Port(25).IsSecure(false))
			.Endpoint(builder => builder.Port(587).IsSecure(false))
		.Build();

		smtpServer = new SmtpServer.SmtpServer(options, serviceProvider);
		smtpServer.StartAsync(cancellationToken);

		logger.LogInformation("SMTP Server started.");
		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken cancellationToken)
	{
		smtpServer.Shutdown();
		logger.LogInformation("SMTP Server stopped.");
		return Task.CompletedTask;
	}
}
