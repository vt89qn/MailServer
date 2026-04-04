using MailServer.Common;
using MailServer.Mail;
using System.Net;

namespace MailServer.Model;

public class ApiResponseModel<T>
{
	public ApiResponseModel()
	{
		Code = 0;
		Msg = "OK";
		Data = default;
	}
	public ApiResponseModel(T apiResponse) : this()
	{
		Data = apiResponse;
	}
	public ApiResponseModel(HttpStatusCode code)
	{
		Code = (int)code;
		Msg = code.ToString();
		Data = default;
	}
	public int Code { get; set; }
	public string Msg { get; set; }
	public T Data { get; set; }

	public void SetHttpStatusCode(HttpStatusCode code)
	{
		Code = (int)code;
		Msg = code.ToString();
	}
}
public class ApiEmailGetResponseModel
{
	public ApiEmailGetResponseModel(MailMessage mailMessage)
	{
		Id = mailMessage.Id;
		Subject = mailMessage.Subject;
		From = mailMessage.From;
		To = mailMessage.To;
		HtmlBody = mailMessage.MimeMessage.HtmlBody;
		RecvDate = mailMessage.RecvDate.ToDateTimeString();
		TextBody = mailMessage.MimeMessage.TextBody;
	}
	public string Id { get; set; }
	public string Subject { get; set; }
	public string From { get; set; }
	public List<string> To { get; set; }
	public string TextBody { get; set; }
	public string HtmlBody { get; set; }
	public string RecvDate { get; set; }
}

public class ApiEmailSendRequestModel
{
	public ApiEmailSendRequestModel()
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
}

public class ApiEmailSendResponseModel
{
	public ApiEmailSendResponseModel()
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
	public string Id { get; set; }
	public string Status { get; set; }
	public string QueuedDate { get; set; }
	public string NextAttemptDate { get; set; }
	public int AttemptCount { get; set; }
	public string LastError { get; set; }
	public string SentDate { get; set; }
}
