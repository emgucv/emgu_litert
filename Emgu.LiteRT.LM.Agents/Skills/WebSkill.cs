//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace Emgu.LiteRT.LM.Agents.Skills
{
    /// <summary>
    /// The web: the weather, Wikipedia, currency conversion and reading a page the user links (no key needed), and with a
    /// Tavily API key (<see cref="TavilyApiKey"/>) general web search and Tavily's page reader. Disabled, it tells the
    /// agent to say that web search is off rather than invent current facts. Requests leave the device.
    /// </summary>
    public sealed class WebSkill : ChatSkill
    {
        private readonly HttpClient _http;

        /// <summary>Create the skill.</summary>
        /// <param name="http">The HTTP client to use, or null for <see cref="AgentTools.DefaultHttpClient"/></param>
        public WebSkill(HttpClient http = null)
        {
            _http = http;
        }

        /// <summary>A Tavily API key (tavily.com) for general web search, or null / empty for the keyless tools only.</summary>
        public string TavilyApiKey { get; set; }

        /// <summary>
        /// Where the user turns web search on and enters the key, in words for the agent: "Settings > Web search" by default.
        /// </summary>
        public string SettingsLocation { get; set; } = "Settings > Web search";

        /// <inheritdoc/>
        public override string Name => "web";

        /// <inheritdoc/>
        public override string Title => "Web search";

        private bool HasKey => !string.IsNullOrWhiteSpace(TavilyApiKey);

        /// <inheritdoc/>
        public override IEnumerable<AITool> Tools(ChatMessageContext context)
        {
            var tools = new List<AITool> { AgentTools.Weather(_http), AgentTools.Wikipedia(_http), AgentTools.ConvertCurrency(_http) };
            if (HasKey)
            {
                tools.Add(AgentTools.TavilySearch(TavilyApiKey, _http));
                // Tavily's reader also reads pages built by scripts.
                tools.Add(AgentTools.TavilyReadPage(TavilyApiKey, _http));
            }
            else
            {
                tools.Add(AgentTools.ReadWebPage(_http));
            }
            return tools;
        }

        /// <inheritdoc/>
        public override Task<string> InstructionsAsync(ChatMessageContext context) => Task.FromResult(HasKey
            ? "General web search is available through search_web. Use it for news and recent events, and cite the sources returned."
            : "General web search is unavailable because no Tavily API key is configured. Wikipedia, reading URLs the user supplies, weather and currency conversion are available without a key. For news, recent stock movements or other requests needing general web search, explicitly tell the user: Add a Tavily API key in " + SettingsLocation + ", then retry. Do not merely say you lack internet or real-time access, and do not invent current facts.");

        /// <inheritdoc/>
        public override string DisabledInstructions =>
            "Web search is turned off. If a request needs current online information, explain that the user must enable Web search; general web search also requires a Tavily API key in " + SettingsLocation + ". Do not invent current facts.";

        /// <inheritdoc/>
        public override string ToolSummary => "weather, Wikipedia, currency conversion" + (HasKey ? ", web search and page reading" : ", reading web pages");
    }
}
