using MimeKit;

namespace MailServer.Mail;

public class MailMessage
{
	public MailMessage()
	{
		To = [];
	}
	public MailMessage(MimeMessage mimeMessage) : this()
	{
		Id = Guid.NewGuid().ToString();
		To.AddRange(mimeMessage.To.OfType<MailboxAddress>().Select(x => x.Address));
		From = mimeMessage.From.OfType<MailboxAddress>().Select(x => x.Address).FirstOrDefault();
		MimeMessage = mimeMessage;
		RecvDate = DateTime.Now;
		Subject = mimeMessage.Subject;
	}

	public string Id { get; set; }
	public string From { get; set; }
	public List<string> To { get; set; }
	public string Subject { get; set; }
	public DateTime RecvDate { get; set; }
	public MimeMessage MimeMessage { get; set; }
}
