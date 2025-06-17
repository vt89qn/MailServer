using MailServer.Common;
using MailServer.Enpoint;
using MailServer.Mail;
using Microsoft.AspNetCore.Http.Json;
using SmtpServer.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWindowsService();

var basePath = builder.Configuration.GetValue<string>("Settings:BasePath");
builder.Host.AddLog(Path.Combine(basePath, "Logs"));

builder.Services.Configure<JsonOptions>(configure => configure.SerializerOptions.AddDefaultSettings());

builder.Services.AddSignalR().AddJsonProtocol(options => options.PayloadSerializerOptions.AddDefaultSettings());

builder.Services.AddSingleton<MailStore>()
				.AddSingleton<IMessageStore>(x => x.GetRequiredService<MailStore>())
				.AddSingleton<IHostedService>(x => x.GetRequiredService<MailStore>())
				.AddSingleton<IHostedService, MailService>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapEmail();

//app.MapFallback(() => Results.Content("404"));

app.Run();
