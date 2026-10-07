using LocalAI.Demo.Components;

var builder = WebApplication.CreateBuilder(args);

// Every model in the "LocalAI" section becomes a Microsoft.Extensions.AI service: IChatClient,
// IEmbeddingGenerator, ISpeechToTextClient, ITextToSpeechClient, plus IReranker and
// IVoiceActivityDetector. Each loads the first time something uses it.
builder.Services.AddLocalAI(builder.Configuration.GetSection("LocalAI"));

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
