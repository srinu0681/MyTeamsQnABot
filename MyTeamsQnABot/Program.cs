using MyTeamsQnABot;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Connector.Authentication;
using Microsoft.Teams.AI;
using Microsoft.Teams.AI.AI.Models;
using Microsoft.Teams.AI.AI.Planners;
using Microsoft.Teams.AI.AI.Prompts;
using Microsoft.Teams.AI.State;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddHttpClient("WebClient", client => client.Timeout = TimeSpan.FromSeconds(600));
builder.Services.AddHttpContextAccessor();

// Prepare Configuration for ConfigurationBotFrameworkAuthentication
var config = builder.Configuration.Get<ConfigOptions>();
builder.Services.AddSingleton(config);

builder.Configuration["MicrosoftAppType"] = config.BOT_TYPE;
builder.Configuration["MicrosoftAppId"] = config.BOT_ID;
builder.Configuration["MicrosoftAppPassword"] = config.BOT_PASSWORD;
builder.Configuration["MicrosoftAppTenantId"] = config.BOT_TENANT_ID;
// Create the Bot Framework Authentication to be used with the Bot Adapter.
builder.Services.AddSingleton<BotFrameworkAuthentication, ConfigurationBotFrameworkAuthentication>();

// Create the Cloud Adapter with error handling enabled.
// Note: some classes expect a BotAdapter and some expect a BotFrameworkHttpAdapter, so
// register the same adapter instance for both types.
builder.Services.AddSingleton<CloudAdapter, AdapterWithErrorHandler>();
builder.Services.AddSingleton<IBotFrameworkHttpAdapter>(sp => sp.GetService<CloudAdapter>());
builder.Services.AddSingleton<BotAdapter>(sp => sp.GetService<CloudAdapter>());

builder.Services.AddSingleton<IStorage, MemoryStorage>();

builder.Services.AddSingleton<OpenAIModel>(sp => new(
    new AzureOpenAIModelOptions(
        config.Azure.OpenAIApiKey,
        config.Azure.OpenAIDeploymentName,
        config.Azure.OpenAIEndpoint
    )
    {
        LogRequests = true
    },
    sp.GetService<ILoggerFactory>()
));

 AzureAISearchDataSourceOptions options = new()
 {
    Name = "azure-ai-search",
    IndexName = "my-documents",
    AzureAISearchApiKey = config.Azure.AISearchApiKey,
    AzureAISearchEndpoint = new Uri(config.Azure.AISearchEndpoint),
    AzureOpenAIApiKey = config.Azure.OpenAIApiKey,
    AzureOpenAIEndpoint = config.Azure.OpenAIEndpoint,
    AzureOpenAIEmbeddingDeployment = config.Azure.OpenAIEmbeddingDeploymentName,
 };

 AzureAISearchDataSource dataSource = new(options);

builder.Services.AddSingleton<Indexer>();

// Create Prompt Manager
builder.Services.AddSingleton<PromptManager>(sp =>
{
    PromptManager promptManager = new(new()
    {
        PromptFolder = "./Prompts"
    });
    promptManager.AddDataSource("azure-ai-search", dataSource);
    return promptManager;
});

// Create planner
builder.Services.AddSingleton<ActionPlanner<TurnState>>(sp =>
{
    ILoggerFactory loggerFactory = sp.GetService<ILoggerFactory>();
    PromptManager prompts = sp.GetService<PromptManager>();

    // Create ActionPlanner
    ActionPlanner<TurnState> planner = new(
        options: new(
            model: sp.GetService<OpenAIModel>(),
            prompts: prompts,
            defaultPrompt: async (context, state, planner) =>
            {
                PromptTemplate template = prompts.GetPrompt("chat");
                return await Task.FromResult(template);
            }
        )
        { LogRepairs = true },
        loggerFactory: loggerFactory
    );

    return planner;
});

// Create the bot as transient. In this case the ASP Controller is expecting an IBot.
builder.Services.AddTransient<IBot>(sp =>
{
    ActionPlanner<TurnState> planner = sp.GetService<ActionPlanner<TurnState>>();

    Application<TurnState> app = new ApplicationBuilder<TurnState>()
        .WithAIOptions(new(planner))
        .WithStorage(sp.GetService<IStorage>())
        .Build();

    app.OnConversationUpdate("membersAdded", async (turnContext, turnState, cancellationToken) =>
    {
        var welcomeText = "How can I help you today?";
        foreach (var member in turnContext.Activity.MembersAdded)
        {
            if (member.Id != turnContext.Activity.Recipient.Id)
            {
                await turnContext.SendActivityAsync(MessageFactory.Text(welcomeText), cancellationToken);
            }
        }
    });

    // This is called when the bot receives a message from the user.
    app.OnActivity("message", ActivityHandlers.CustomMessageHandler);

    return app;
});

var app = builder.Build();

ActivityHandlers.Configure(app.Services);

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseStaticFiles();
app.UseRouting();
app.UseEndpoints(endpoints =>
{
    endpoints.MapControllers();
});

app.Run();