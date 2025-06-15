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
	public List<string> From { get; set; }
	public List<string> To { get; set; }
	public string TextBody { get; set; }
	public string HtmlBody { get; set; }
	public string RecvDate { get; set; }
}
