namespace MailServer.Model;

public class ReceiveEmailModel
{
	public string Id { get; set; }
	public string Subject { get; set; }
	public string From { get; set; }
	public List<string> To { get; set; }
	public string TextBody { get; set; }
	public string HtmlBody { get; set; }
	public DateTime RecvDate { get; set; }
}
