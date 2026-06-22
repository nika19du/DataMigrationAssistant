using DataMigrationAssistant.Blazor.Services;
using DataMigrationAssistant.Core.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSignalR(o => o.MaximumReceiveMessageSize = 10 * 1024 * 1024);

builder.Services.AddDataMigrationCore();
builder.Services.AddMigrationAgents();
builder.Services.AddScoped<TempFileService>();
builder.Services.AddScoped<DownloadService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<DataMigrationAssistant.Blazor.App>()
    .AddInteractiveServerRenderMode();

app.Run();
