using MimeKit;

namespace MailServer.Mail;

public class MailMessage
{
	public MailMessage()
	{
		From = [];
		To = [];
	}
	public MailMessage(MimeMessage mimeMessage) : this()
	{
		To.AddRange(mimeMessage.To.OfType<MailboxAddress>().Select(x => x.Address));
		From.AddRange(mimeMessage.From.OfType<MailboxAddress>().Select(x => x.Address));
		MimeMessage = mimeMessage;
		RecvDate = DateTime.Now;
	}

	public List<string> From { get; set; }
	public List<string> To { get; set; }
	public DateTime RecvDate { get; set; }
	public MimeMessage MimeMessage { get; set; }
}
