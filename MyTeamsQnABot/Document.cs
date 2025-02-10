using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyTeamsQnABot
{
    public class Document
    {
        [SimpleField(IsKey = true, IsFilterable = true, IsSortable = true)]
        public string DocId { get; set; }

        [SearchableField(IsFilterable = true, IsSortable = true)]
        public string DocTitle { get; set; }

        [SearchableField(AnalyzerName = LexicalAnalyzerName.Values.EnLucene)]
        public string Description { get; set; }

        [VectorSearchField(VectorSearchDimensions = 1536, VectorSearchProfileName = "my-vector-config")]
        public IReadOnlyList<float>? DescriptionVector { get; set; } = null;

        [JsonPropertyName("@search.action")]
        public string SearchAction { get; set; }

        public override string ToString()
        {
            return JsonSerializer.Serialize(this);
        }
    }

    class EmbeddingResponse
    {
        public List<EmbeddingData> Data { get; set; }
    }

    class EmbeddingData
    {
        public float[] Embedding { get; set; }
    }
}
