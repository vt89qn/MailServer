using MailServer.Common;
using MailServer.Enpoint;
using MailServer.Mail;
using Microsoft.AspNetCore.Http.Json;
using SmtpServer.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWindowsService();

SystemConst.BasePath = builder.Configuration["basePath"];
SystemConst.KeysPath = Path.Combine(SystemConst.BasePath, "Keys");
builder.Host.AddLog(Path.Combine(SystemConst.BasePath, "Logs"));


builder.Services.Configure<JsonOptions>(configure => configure.SerializerOptions.AddDefaultSettings());
builder.Services.Configure<SmtpSenderOptions>(builder.Configuration.GetSection(SmtpSenderOptions.SectionName));
builder.Services.Configure<DkimOptions>(builder.Configuration.GetSection(DkimOptions.SectionName));
builder.Services.Configure<OutboundQueueOptions>(builder.Configuration.GetSection(OutboundQueueOptions.SectionName));

builder.Services.AddSignalR().AddJsonProtocol(options => options.PayloadSerializerOptions.AddDefaultSettings());

builder.Services.AddSingleton<EmailSender>()
				.AddSingleton<OutboundEmailQueue>()
				.AddSingleton<MailStore>()
				.AddSingleton<IMessageStore>(x => x.GetRequiredService<MailStore>())
				.AddSingleton<IHostedService>(x => x.GetRequiredService<OutboundEmailQueue>())
				.AddSingleton<IHostedService>(x => x.GetRequiredService<MailStore>())
				.AddSingleton<IHostedService, MailService>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapEmail();

//app.MapFallback(() => Results.Content("404"));

app.Run();
