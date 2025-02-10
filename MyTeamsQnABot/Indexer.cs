using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace MyTeamsQnABot
{
    public class Indexer(ConfigOptions _config)
    {
        private readonly string _indexName = "my-documents";

        public async Task Delete()
        {
            await DeleteIndexAsync(_indexName);
        }

        public async Task CreateIndexAndUploadDocument(string filePath)
        {
            await CreateIndexAsync(_indexName);
            var document = await GetDataAsync(filePath);
            await UploadDocumentAsync(document);
        }

        private async Task CreateIndexAsync(string indexName)
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("api-key", _config.Azure.AISearchApiKey);

            var indexSchema = new
            {
                name = indexName,
                fields = new object[]
                {
                    new { name = "DocId", type = "Edm.String", key = true, filterable = true, sortable = true },
                    new { name = "DocTitle", type = "Edm.String", searchable = true, filterable = true, sortable = true },
                    new { name = "Description", type = "Edm.String", searchable = true, analyzer = "en.lucene" },
                    new { name = "DescriptionVector", type = "Collection(Edm.Single)", searchable = true, dimensions = 1536, vectorSearchProfile = "my-vector-config", retrievable = true }
                },
                corsOptions = new { allowedOrigins = new[] { "*" } },
                vectorSearch = new
                {
                    algorithms = new[] { new { name = "vector-search-algorithm", kind = "hnsw" } },
                    profiles = new[] { new { name = "my-vector-config", algorithm = "vector-search-algorithm" } }
                }
            };

            string uri = $"{_config.Azure.AISearchEndpoint}/indexes('{indexName}')?api-version=2024-07-01";
            var content = new StringContent(JsonSerializer.Serialize(indexSchema), Encoding.UTF8, "application/json");
            var response = await client.PutAsync(uri, content);

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception("Failed to create the search index");
            }
            await Task.Delay(5000); // Wait for 5 seconds
        }

        private async Task DeleteIndexAsync(string indexName)
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("api-key", _config.Azure.AISearchApiKey);

            string uri = $"{_config.Azure.AISearchEndpoint}/indexes('{indexName}')?api-version=2024-07-01";
            var response = await client.DeleteAsync(uri);

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception("Failed to delete the search index");
            }
        }

#nullable enable

        private async Task<Document?> GetDataAsync(string? filePath = null)
        {
            if (String.IsNullOrEmpty(filePath))
            {
                return null;
            }

            var content = await File.ReadAllTextAsync(filePath);

            var vector = await GetEmbeddingsAsync(content);

            var document = new Document
            {
                DocId = "1",
                DocTitle = Path.GetFileName(filePath),
                Description = content,
                DescriptionVector = vector,
                SearchAction = "mergeOrUpload"
            };

            return document;
        }

        private async Task UploadDocumentAsync(Document document)
        {
            List<Document> documents = [document];
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("api-key", _config.Azure.AISearchApiKey);

            string uri = $"{_config.Azure.AISearchEndpoint}/indexes('{_indexName}')/docs/search.index?api-version=2024-07-01";
            var body = new { value = documents };
            var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            var response = await client.PostAsync(uri, content);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"Failed to upload document. Error: {error}");
            }
        }

        private async Task<float[]> GetEmbeddingsAsync(string text)
        {
            using var client = new HttpClient();
            string uri;
            HttpRequestMessage request;

            uri = $"{_config.Azure.OpenAIEndpoint}/openai/deployments/{_config.Azure.OpenAIEmbeddingDeploymentName}/embeddings?api-version=2024-10-21";
            request = new HttpRequestMessage(HttpMethod.Post, uri);
            request.Headers.Add("api-key", _config.Azure.OpenAIApiKey);
            var body = new { input = text };
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"Failed to retrieve embeddings. Error: {error}");
            }

            var embeddingsJson = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<EmbeddingResponse>(embeddingsJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return result?.Data?[0]?.Embedding ?? [];
        }
    }
}
