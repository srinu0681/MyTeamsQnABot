using Microsoft.Bot.Builder;
using Microsoft.Teams.AI;
using Microsoft.Teams.AI.AI.Planners;
using Microsoft.Teams.AI.AI.Prompts;
using Microsoft.Teams.AI.State;
using Microsoft.Bot.Schema;
using System.Net.NetworkInformation;
using System.Text.Json;

namespace MyTeamsQnABot
{
    public class ActivityHandlers
    {
        private static ConfigOptions? _configOptions;
        private static Indexer? _indexer;
        private static PromptManager? _promptManager;
        private static ActionPlanner<TurnState>? _planner;
        private static string DownloadsFolder = "Files";

        public static void Configure(IServiceProvider serviceProvider)
        {
            _configOptions = serviceProvider.GetService<ConfigOptions>();
            _indexer = serviceProvider.GetService<Indexer>();
            _promptManager = serviceProvider.GetService<PromptManager>();
            _planner = serviceProvider.GetService<ActionPlanner<TurnState>>();
        }

        /// <summary>
        /// Handles custom messages.
        /// </summary>
        public static RouteHandler<TurnState> CustomMessageHandler = static async (ITurnContext turnContext, TurnState turnState, CancellationToken cancellationToken) =>
        {
            // If the message has a file attachment, download the file and upload it to the AI search service
            Attachment? attachment = turnContext.Activity.Attachments.FirstOrDefault(attachment => attachment.ContentType == "application/vnd.microsoft.teams.file.download.info");
            if (attachment != null)
            {
                var downloadUrl = "";

                // Get download url
                JsonDocument document = JsonDocument.Parse(attachment.Content.ToString());
                JsonElement root = document.RootElement;

                if (root.TryGetProperty("downloadUrl", out JsonElement downloadUrlElement))
                {
                    downloadUrl = downloadUrlElement.GetString();
                }

                if (String.IsNullOrWhiteSpace(downloadUrl))
                {
                    await turnContext.SendActivityAsync(MessageFactory.Text($"Can not retrieve the downloadUrl"), cancellationToken);
                    return;
                }

                // Download the file content
                var client = new HttpClient();
                var response = await client.GetAsync(downloadUrl, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    var responseMessage = await response.Content.ReadAsStringAsync(cancellationToken);
                    await turnContext.SendActivityAsync(MessageFactory.Text($"File download failed. Reason: {responseMessage}"), cancellationToken);
                    return;
                }

                var fileContent = await response.Content.ReadAsStringAsync(cancellationToken);

                // Save the document to Files directory.
                var filePath = Path.Combine(DownloadsFolder, attachment.Name);
                if (!Directory.Exists(DownloadsFolder))
                    Directory.CreateDirectory(DownloadsFolder);

                await File.WriteAllTextAsync(filePath, fileContent, cancellationToken);

                // Upload document to AI search service
                await _indexer.CreateIndexAndUploadDocument(filePath);

                //// Send reply
                //var reply = MessageFactory.Text($"Attachment of {attachment.ContentType} type and size of {response.Content.Headers.ContentLength} bytes received.");
                //await turnContext.SendActivityAsync(reply, cancellationToken);
            }

            // Get the response from the planner (LLM)
            var promptResponse = await _planner.CompletePromptAsync(turnContext, turnState, _promptManager.GetPrompt("chat"), null, cancellationToken);

            await turnContext.SendActivityAsync(MessageFactory.Text(promptResponse.Message.Content.ToString()), cancellationToken);
        };
    }
}
