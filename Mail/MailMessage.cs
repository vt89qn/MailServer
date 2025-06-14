using MimeKit;

namespace MailServer.Mail;

public class MailMessage
{
	public MailMessage()
	{
	}
	public MailMessage(MimeMessage message) : this()
	{
		Message = message;
		if (message.To.FirstOrDefault() is MailboxAddress to)
		{
			To = to.Address;
		}
		if (message.From.FirstOrDefault() is MailboxAddress from)
		{
			From = from.Address;
		}
		To = message.To.ToString();
		TextBody = message.TextBody;
		HtmlBody = message.HtmlBody;
		RecvDate = DateTime.UtcNow;
	}

	public string From { get; set; }
	public string To { get; set; }
	public string TextBody { get; set; }
	public string HtmlBody { get; set; }
	public DateTime RecvDate { get; set; }
	public MimeMessage Message { get; set; }
}
