using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BunkerIs_dl.Json
{
    // Root myDeserializedClass = JsonConvert.DeserializeObject<Root>(myJsonResponse);
    public class Album
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("identifier")]
        public string Identifier { get; set; }

        [JsonProperty("public")]
        public int Public { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }
    }

    public class AlbumFile
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("size")]
        public long Size { get; set; }

        [JsonProperty("timestamp")]
        public int Timestamp { get; set; }

        [JsonProperty("cdn")]
        public string Cdn { get; set; }
    }

    public class PageProps
    {
        [JsonProperty("album")]
        public Album Album { get; set; }

        [JsonProperty("files")]
        public List<AlbumFile> Files { get; set; }
    }

    public class Props
    {
        [JsonProperty("pageProps")]
        public PageProps PageProps { get; set; }

        [JsonProperty("__N_SSG")]
        public bool NSSG { get; set; }
    }

    public class Query
    {
        [JsonProperty("albumId")]
        public string AlbumId { get; set; }
    }

    public class PageData
    {
        [JsonProperty("props")]
        public Props Props { get; set; }

        [JsonProperty("page")]
        public string Page { get; set; }

        [JsonProperty("query")]
        public Query Query { get; set; }

        [JsonProperty("buildId")]
        public string BuildId { get; set; }

        [JsonProperty("isFallback")]
        public bool IsFallback { get; set; }

        [JsonProperty("gsp")]
        public bool Gsp { get; set; }

        [JsonProperty("scriptLoader")]
        public List<object> ScriptLoader { get; set; }
    }
}
