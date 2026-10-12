//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

#if WINDOWS || IOS || ANDROID || MACCATALYST

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace Maui.Demo.Lite
{
    /// <summary>
    /// The tools the LiteRT-LM chat page's agent can call (Microsoft.Extensions.AI functions): the date and time, the
    /// user's location (when they allow it), a Wikipedia search (no key needed), and - with a Tavily API key - a web
    /// search and a web page reader. Results are cut short, since an on-device model has a small context.
    /// </summary>
    internal static class AgentTools
    {
        private const int MaxResultLength = 1500;
        private const int TopArticleLength = 1100;
        private const int OtherArticleLength = 300;

        private static readonly HttpClient Http = CreateHttpClient();

        private static HttpClient CreateHttpClient()
        {
            HttpClient client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            // Wikipedia asks API clients to identify themselves.
            client.DefaultRequestHeaders.UserAgent.ParseAdd("EmguLiteRTMauiDemo/1.0 (https://www.emgu.com)");
            return client;
        }

        /// <summary>
        /// The tools to give the agent for a message: the date and time always; the user's location when they allow it;
        /// the search tools with web search on - the web ones only with a Tavily API key.
        /// </summary>
        public static IList<AITool> Create(string tavilyApiKey, bool webSearch, bool location)
        {
            List<AITool> tools = new List<AITool>
            {
                AIFunctionFactory.Create(GetCurrentDateTime, "get_current_date_time",
                    "Returns the current local date and time.")
            };
            if (location)
            {
                tools.Add(AIFunctionFactory.Create(GetCurrentLocationAsync, "get_current_location",
                    "Returns where the user is now: their town or city, region and country, and approximate coordinates. Use it for questions about things near the user, local weather, or their time zone."));
            }
            if (!webSearch)
                return tools;
            tools.Add(AIFunctionFactory.Create(SearchWikipediaAsync, "search_wikipedia",
                "Searches English Wikipedia and returns the introductions of the best matching articles. Use it for facts about people, places, events, science and history."));
            if (!string.IsNullOrWhiteSpace(tavilyApiKey))
            {
                string key = tavilyApiKey.Trim();
                tools.Add(AIFunctionFactory.Create(
                    ([Description("What to search for")] string query, CancellationToken cancellationToken) => SearchWebAsync(key, query, cancellationToken),
                    "search_web",
                    "Searches the web and returns extracts of the best matching pages. Use it for news, recent events and anything Wikipedia doesn't cover."));
                tools.Add(AIFunctionFactory.Create(
                    ([Description("The address of the page")] string url, CancellationToken cancellationToken) => ReadWebPageAsync(key, url, cancellationToken),
                    "read_web_page",
                    "Returns the text of a web page."));
            }
            return tools;
        }

        /// <summary>
        /// A short description of a tool call, shown in the transcript.
        /// </summary>
        public static string Describe(FunctionCallContent call)
        {
            string Argument(string name) =>
                call.Arguments != null && call.Arguments.TryGetValue(name, out object value) && value != null
                    ? (value is JsonElement element && element.ValueKind == JsonValueKind.String ? element.GetString() : value.ToString())
                    : "";
            switch (call.Name)
            {
                case "get_current_date_time": return "Checking the date and time";
                case "get_current_location": return "Getting your location";
                case "search_wikipedia": return "Searching Wikipedia for “" + Argument("query") + "”";
                case "search_web": return "Searching the web for “" + Argument("query") + "”";
                case "read_web_page": return "Reading " + Argument("url");
                default: return "Calling " + call.Name;
            }
        }

        private static string GetCurrentDateTime()
        {
            return DateTimeOffset.Now.ToString("dddd, d MMMM yyyy, HH:mm (zzz)", System.Globalization.CultureInfo.InvariantCulture);
        }

        // The user's location at about city level: the device's position (a recent known one, else a quick low-accuracy
        // fix), turned into a place name by the platform's geocoder (online: Apple / Google). Coordinates are rounded to
        // ~1 km. Permission is asked for the first time it is used; geolocation and permissions want the UI thread.
        private static async Task<string> GetCurrentLocationAsync(CancellationToken cancellationToken)
        {
            try
            {
                PermissionStatus status = await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    PermissionStatus current = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
                    return current == PermissionStatus.Granted ? current : await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
                });
                if (status != PermissionStatus.Granted)
                    return "The user hasn't allowed this app to use their location.";

                Location location = await MainThread.InvokeOnMainThreadAsync(() => Geolocation.Default.GetLastKnownLocationAsync());
                if (location == null || DateTimeOffset.UtcNow - location.Timestamp > TimeSpan.FromMinutes(30))
                {
                    GeolocationRequest request = new GeolocationRequest(GeolocationAccuracy.Low, TimeSpan.FromSeconds(15));
                    location = await MainThread.InvokeOnMainThreadAsync(() => Geolocation.Default.GetLocationAsync(request, cancellationToken))
                        ?? location;
                }
                if (location == null)
                    return "The device couldn't determine its location.";

                string coordinates = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "latitude {0:F2}, longitude {1:F2}", location.Latitude, location.Longitude);
                string place = null;
                try
                {
                    Placemark placemark = null;
                    foreach (Placemark p in await Geocoding.Default.GetPlacemarksAsync(location.Latitude, location.Longitude))
                    {
                        placemark = p;
                        break;
                    }
                    if (placemark != null)
                    {
                        List<string> parts = new List<string>();
                        foreach (string part in new[] { placemark.SubLocality, placemark.Locality, placemark.AdminArea, placemark.CountryName })
                            if (!string.IsNullOrWhiteSpace(part) && !parts.Contains(part))
                                parts.Add(part);
                        place = string.Join(", ", parts);
                    }
                }
                catch (Exception)
                {
                    // No geocoder (e.g. Windows without a map service key) or no network: the coordinates only.
                }
                return string.IsNullOrEmpty(place)
                    ? "The user is at " + coordinates + "."
                    : "The user is in " + place + " (" + coordinates + ").";
            }
            catch (FeatureNotEnabledException)
            {
                return "Location services are turned off on this device.";
            }
            catch (FeatureNotSupportedException)
            {
                return "This device can't determine its location.";
            }
            catch (PermissionException)
            {
                return "The user hasn't allowed this app to use their location.";
            }
        }

        private static async Task<string> SearchWikipediaAsync(
            [Description("What to search for, e.g. a name or a topic")] string query,
            CancellationToken cancellationToken)
        {
            // Find the best matching articles, then get their introductions as plain text.
            string searchUrl = "https://en.wikipedia.org/w/api.php?action=query&list=search&srlimit=3&format=json&srsearch="
                + Uri.EscapeDataString(query);
            List<string> titles = new List<string>();
            using (JsonDocument search = JsonDocument.Parse(await Http.GetStringAsync(searchUrl, cancellationToken)))
            {
                foreach (JsonElement result in search.RootElement.GetProperty("query").GetProperty("search").EnumerateArray())
                    titles.Add(result.GetProperty("title").GetString());
            }
            if (titles.Count == 0)
                return "No Wikipedia articles found for \"" + query + "\".";

            string extractsUrl = "https://en.wikipedia.org/w/api.php?action=query&prop=extracts&exintro=1&explaintext=1&redirects=1&format=json&titles="
                + Uri.EscapeDataString(string.Join("|", titles));
            Dictionary<string, string> extracts = new Dictionary<string, string>();
            using (JsonDocument pages = JsonDocument.Parse(await Http.GetStringAsync(extractsUrl, cancellationToken)))
            {
                foreach (JsonProperty page in pages.RootElement.GetProperty("query").GetProperty("pages").EnumerateObject())
                {
                    if (page.Value.TryGetProperty("extract", out JsonElement extract))
                        extracts[page.Value.GetProperty("title").GetString()] = extract.GetString();
                }
            }

            // Most of the room goes to the best match: the answer is often further into its introduction.
            StringBuilder text = new StringBuilder();
            for (int i = 0; i < titles.Count; i++)
            {
                string title = titles[i];
                if (!extracts.TryGetValue(title, out string extract) || string.IsNullOrWhiteSpace(extract))
                    continue;
                text.Append("Article: ").Append(title)
                    .Append(" (https://en.wikipedia.org/wiki/").Append(Uri.EscapeDataString(title.Replace(' ', '_'))).Append(")\n")
                    .Append(Shorten(extract, i == 0 ? TopArticleLength : OtherArticleLength)).Append("\n\n");
            }
            return text.Length > 0 ? text.ToString().TrimEnd() : "No Wikipedia articles found for \"" + query + "\".";
        }

        private static async Task<string> SearchWebAsync(string apiKey, string query, CancellationToken cancellationToken)
        {
            string body = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                { "query", query },
                { "max_results", 3 },
                { "search_depth", "basic" }
            }, AgentToolsJsonContext.Default.DictionaryStringObject);
            using (JsonDocument response = await PostTavilyAsync(apiKey, "search", body, cancellationToken))
            {
                StringBuilder text = new StringBuilder();
                JsonElement results = response.RootElement.GetProperty("results");
                int count = Math.Max(1, results.GetArrayLength());
                foreach (JsonElement result in results.EnumerateArray())
                {
                    text.Append("Page: ").Append(result.GetProperty("title").GetString())
                        .Append(" (").Append(result.GetProperty("url").GetString()).Append(")\n")
                        .Append(Shorten(result.GetProperty("content").GetString(), MaxResultLength / count)).Append("\n\n");
                }
                return text.Length > 0 ? text.ToString().TrimEnd() : "No results found for \"" + query + "\".";
            }
        }

        private static async Task<string> ReadWebPageAsync(string apiKey, string url, CancellationToken cancellationToken)
        {
            string body = JsonSerializer.Serialize(new Dictionary<string, object> { { "urls", new[] { url } } },
                AgentToolsJsonContext.Default.DictionaryStringObject);
            using (JsonDocument response = await PostTavilyAsync(apiKey, "extract", body, cancellationToken))
            {
                foreach (JsonElement result in response.RootElement.GetProperty("results").EnumerateArray())
                    return Shorten(result.GetProperty("raw_content").GetString(), MaxResultLength);
                return "Could not read " + url + ".";
            }
        }

        private static async Task<JsonDocument> PostTavilyAsync(string apiKey, string endpoint, string body, CancellationToken cancellationToken)
        {
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "https://api.tavily.com/" + endpoint))
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                using (HttpResponseMessage response = await Http.SendAsync(request, cancellationToken))
                {
                    string json = await response.Content.ReadAsStringAsync(cancellationToken);
                    if (!response.IsSuccessStatusCode)
                        throw new InvalidOperationException(string.Format("Tavily returned {0}", (int)response.StatusCode));
                    return JsonDocument.Parse(json);
                }
            }
        }

        private static string Shorten(string text, int maxLength)
        {
            text = (text ?? "").Trim();
            return text.Length <= maxLength ? text : text.Substring(0, maxLength).TrimEnd() + "...";
        }
    }

    // Source-generated JSON for the Tavily request bodies (no reflection, so it survives trimming).
    [System.Text.Json.Serialization.JsonSerializable(typeof(Dictionary<string, object>))]
    [System.Text.Json.Serialization.JsonSerializable(typeof(string[]))]
    [System.Text.Json.Serialization.JsonSerializable(typeof(int))]
    [System.Text.Json.Serialization.JsonSerializable(typeof(string))]
    internal partial class AgentToolsJsonContext : System.Text.Json.Serialization.JsonSerializerContext
    {
    }
}

#endif
