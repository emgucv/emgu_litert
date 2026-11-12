// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using Emgu.LiteRT.LM.Chat;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
#if IOS || MACCATALYST
using Microsoft.Maui.Essentials.AI;
#endif

// CA1416: the model's APIs need iOS / Mac Catalyst 26; every use is behind IsSupported (the library itself targets 15).
#pragma warning disable CA1416

namespace Emgu.LiteRT.LM.Maui.Apple;

/// <summary>
/// Apple's on-device language model (the Foundation Models framework, "Apple Intelligence") in place of a model of this app's
/// own: the remote "Apple Intelligence" option of the model list (<see cref="Option"/>, which the app adds to the page's
/// models). It chats through the same agent as the models on the device, with the same saved chats and compaction, over
/// Microsoft.Maui.Essentials.AI's <c>AppleIntelligenceChatClient</c>. Nothing is downloaded by the app and nothing leaves
/// the device. It needs iOS 26 or macOS 26 or later (Mac Catalyst too), on a device that has Apple Intelligence, turned on in
/// the system's settings (<see cref="IsSupported"/> is the operating system's part; the rest is asked when the model is
/// connected, and said in words: not eligible, turned off, still being prepared). The model's context is small (about 4,096
/// tokens, all of it counted: the instructions, the tools' descriptions, their results and the conversation, so the tools are
/// cut down to a few short ones, <see cref="AdjustOptions"/>) and it doesn't think aloud.
/// </summary>
public sealed class AppleIntelligenceFeature : ChatFeature, IRemoteChatModel
{
    /// <summary>The id of the option made when none is given.</summary>
    public const string DefaultId = "AppleIntelligence";

    // What the model takes in: about 4,096 tokens for the conversation and its reply.
    private const int ContextTokens = 4096;
    private const int SummaryMaxTokens = 400;

    private Label _status;
    private string _availability = string.Empty;

    /// <summary>Create the feature, for a remote option of the app's or one of its own.</summary>
    /// <param name="option">The remote option of the model list this feature connects (its <see cref="ChatModelOption.Remote"/> is set); null for a new "Apple Intelligence" option with the <see cref="DefaultId"/></param>
    public AppleIntelligenceFeature(ChatModelOption option = null)
    {
        if (option != null && !option.IsRemote)
            throw new ArgumentException("The Apple Intelligence option must be a remote one (no model on the device)", nameof(option));
        Option = option ?? new ChatModelOption(DefaultId, "Apple Intelligence")
        {
            // Tools within the small window: AdjustOptions keeps the few that fit (the page's 24 and its instructions are 3,654 tokens).
            CanUseTools = true,
            CanSummarize = true,
            CanThink = false,
            AcceptsImages = false,
            ContextWindow = ContextTokens
        };
        Option.Remote = this;
        UpdateOptionDetail();
    }

    /// <summary>
    /// Whether this operating system has Apple's on-device model (iOS or Mac Catalyst 26 and later); an app adds
    /// <see cref="Option"/> to its models only when it does. The device must still be eligible and have Apple Intelligence
    /// turned on: <see cref="ConnectAsync"/> says when it isn't.
    /// </summary>
    public static bool IsSupported =>
#if IOS || MACCATALYST
        OperatingSystem.IsIOSVersionAtLeast(26) || OperatingSystem.IsMacCatalystVersionAtLeast(26);
#else
        false;
#endif

    /// <inheritdoc/>
    public override string Name => "apple_intelligence";

    /// <inheritdoc/>
    public override string Title => "Apple Intelligence";

    /// <inheritdoc/>
    public override bool PersistEnabled => false;

    /// <summary>The remote option of the model list this feature connects: add it to the page's models.</summary>
    public ChatModelOption Option { get; }

    /// <summary>True while Apple Intelligence is the model chosen.</summary>
    public bool IsSelected => Host?.Model == Option;

    /// <inheritdoc/>
    public override void RegisterServices(ChatFeatureServices services) => services.Add(this);

    /// <inheritdoc/>
    public override void OnAttached(IChatPageHost host)
    {
        base.OnAttached(host);
        UpdateOptionDetail();
        _ = CheckAvailabilityAsync();
    }

    /// <inheritdoc/>
    public override View BuildSettingsSection()
    {
        _status = new Label { Text = _availability };
        ChatResources.ApplyStyle(_status, "ChatSectionNoteStyle");
        var check = new Button { Text = "Check again" };
        check.Clicked += (s, e) => _ = CheckAvailabilityAsync();
        return new VerticalStackLayout
        {
            Spacing = 6,
            Children =
            {
                ChatPage.SectionTitle("Apple Intelligence"),
                ChatPage.Note("Chat with Apple's own on-device model instead of a model of this app's: nothing is downloaded by " +
                    "the app and nothing leaves this device. Pick \"" + Option.DisplayName + "\" in the model list. It needs " +
                    "Apple Intelligence, turned on in the system's settings, and its context is small (about 4,096 tokens, " +
                    "so long chats are summarized often and the tools take much of it)."),
                check,
                _status
            }
        };
    }

    // ---------- availability ----------

    // The model's state, in words: null when it is ready.
    private async Task<string> ProblemAsync(CancellationToken cancellationToken)
    {
#if IOS || MACCATALYST
        if (!IsSupported)
            return "Apple Intelligence needs iOS 26 or macOS 26 or later.";
        try
        {
            ChatModelStatus status = await AppleIntelligenceChatClient.GetModelStatusAsync(cancellationToken);
            switch (status.Availability)
            {
                case ChatModelAvailability.Available:
                    Apply(status);
                    return null;
                case ChatModelAvailability.NotReady:
                case ChatModelAvailability.Downloading:
                    return "Apple Intelligence's model isn't ready yet: the system may still be preparing it. Try again in a few minutes.";
                case ChatModelAvailability.NotSupported:
                    return "This device can't run Apple Intelligence.";
                case ChatModelAvailability.Disabled:
                    return "Apple Intelligence is turned off: turn it on in the system's Settings > Apple Intelligence & Siri.";
                default:
                    return "Apple Intelligence isn't available (" + status.Availability + ").";
            }
        }
        catch (Exception ex)
        {
            if (cancellationToken.IsCancellationRequested)
                return "The connection was cancelled.";
            System.Diagnostics.Debug.WriteLine("Apple Intelligence status failed: " + ex);
            return "Couldn't ask Apple Intelligence: " + ex.Message;
        }
#else
        await Task.CompletedTask;
        return "Apple Intelligence is only on Apple devices.";
#endif
    }

#if IOS || MACCATALYST
    // What the model takes, as its provider says (null: not advertised, so the option's own).
    private void Apply(ChatModelStatus status)
    {
        if (status.Capabilities is { } capabilities)
        {
            Option.AcceptsImages = capabilities.Contains(ChatModelCapability.ImageInput);
            Option.CanUseTools = capabilities.Contains(ChatModelCapability.ToolCalling);
        }
        if (IsSelected)
            Host.ModelCapabilitiesChanged();
    }
#endif

    private async Task CheckAvailabilityAsync()
    {
        string problem = await ProblemAsync(CancellationToken.None);
        _availability = problem ?? "Apple Intelligence is ready.";
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_status != null)
                _status.Text = _availability;
            UpdateOptionDetail(problem);
        });
    }

    private void UpdateOptionDetail(string problem = null)
    {
        Option.Detail = problem ?? "Apple's on-device model · nothing is downloaded and nothing leaves this device";
        Host?.RefreshModelList();
    }

    // ---------- IRemoteChatModel ----------

    /// <summary>Check that Apple Intelligence is available and learn what it takes (the page's connection before the first message).</summary>
    /// <param name="cancellationToken">Cancels it</param>
    /// <returns>Null when the model is ready, else why it isn't</returns>
    public async Task<string> ConnectAsync(CancellationToken cancellationToken)
    {
        string problem = await ProblemAsync(cancellationToken);
        MainThread.BeginInvokeOnMainThread(() => UpdateOptionDetail(problem));
        return problem;
    }

    /// <summary>The client for Apple's model (the page adds function invocation).</summary>
    public IChatClient CreateChatClient() =>
#if IOS || MACCATALYST
        new AppleIntelligenceChatClient();
#else
        throw new PlatformNotSupportedException("Apple Intelligence is only on Apple devices.");
#endif

    // The tools Apple's small window can hold, in the order they are taken: every tool and its description counts toward the
    // 4,096 tokens (all 24 of the page's, with its instructions, are 3,654 tokens before a word is said), so a message gets
    // these, then the page's others while the budget lasts, and no stubs for the tool groups ("use_camera", ...).
    private static readonly string[] ToolPriority =
    {
        "get_current_date_time", "calculate", "convert_units", "search_wikipedia", "read_web_page", "get_weather",
        "convert_currency", "search_web"
    };

    // The tools' names, descriptions and parameters: about 2.3 characters a token as the model counts them, so ~700 tokens.
    private const int ToolBudgetCharacters = 1600;

    // A tool's result: ~375 tokens at most (a web page's text was 1,500 tokens).
    private const int ToolResultCharacters = 1200;

    private const string CompactInstructions = "You are a helpful assistant on the user's device. Use your tools for the date, " +
        "arithmetic, units and facts you are not sure of, and base your answer on what they return. Answer stories, poems and " +
        "opinions yourself, without tools. Keep answers short.";

    /// <inheritdoc/>
    public void AdjustOptions(ChatOptions options)
    {
        if (options == null)
            return;
        // The model doesn't think aloud: the Think chip's reasoning request means nothing to it.
        options.Reasoning = null;
        if (options.Tools is not { Count: > 0 })
            return;
        // Short instructions, the tools that fit with their results cut short.
        options.Instructions = CompactInstructions;
        var chosen = new List<AITool>();
        int used = 0;
        foreach (AITool tool in options.Tools
            .Where(t => !t.Name.StartsWith("use_", StringComparison.Ordinal))
            .OrderBy(t => Array.IndexOf(ToolPriority, t.Name) is int i and >= 0 ? i : ToolPriority.Length))
        {
            AITool compact = tool is AIFunction function ? new CompactFunction(function, ToolResultCharacters) : tool;
            int size = compact.Name.Length + (compact.Description?.Length ?? 0)
                + ((compact as AIFunctionDeclaration)?.JsonSchema.GetRawText().Length ?? 0);
            if (chosen.Count > 0 && used + size > ToolBudgetCharacters)
                continue;
            used += size;
            chosen.Add(compact);
        }
        options.Tools = chosen;
    }

    // A tool as the small window takes it: a short description (its first sentence), a schema without the parameters'
    // descriptions (names, types, enums and which are required stay), and a result cut to a length the window can take.
    private sealed class CompactFunction : DelegatingAIFunction
    {
        private const int DescriptionCharacters = 110;
        private readonly int _maxResultCharacters;

        public CompactFunction(AIFunction inner, int maxResultCharacters) : base(inner)
        {
            _maxResultCharacters = maxResultCharacters;
            string description = inner.Description ?? string.Empty;
            int stop = description.IndexOf(". ", StringComparison.Ordinal);
            if (stop > 0)
                description = description.Substring(0, stop + 1);
            Description = description.Length > DescriptionCharacters ? description.Substring(0, DescriptionCharacters) + "..." : description;
            JsonNode schema = JsonNode.Parse(inner.JsonSchema.GetRawText());
            Strip(schema);
            JsonSchema = JsonSerializer.SerializeToElement(schema);
        }

        public override string Description { get; }

        public override JsonElement JsonSchema { get; }

        // Descriptions inside the schema go (a string "description"; a parameter named description is an object and stays).
        private static void Strip(JsonNode node)
        {
            if (node is JsonObject obj)
            {
                if (obj["description"] is JsonValue)
                    obj.Remove("description");
                foreach (KeyValuePair<string, JsonNode> property in obj.ToList())
                    if (property.Value != null)
                        Strip(property.Value);
            }
            else if (node is JsonArray array)
                foreach (JsonNode item in array)
                    if (item != null)
                        Strip(item);
        }

        protected override async ValueTask<object> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
        {
            object result = await base.InvokeCoreAsync(arguments, cancellationToken).ConfigureAwait(false);
            string text = result as string ?? result?.ToString();
            return text != null && text.Length > _maxResultCharacters ? text.Substring(0, _maxResultCharacters) + "... (cut)" : result;
        }
    }

    /// <inheritdoc/>
    public bool ShouldResend(Exception error, ChatOptions options) => false;

    /// <summary>The conversation's summary options: a short summary, as the context is small.</summary>
    public ChatOptions SummaryOptions => new ChatOptions { MaxOutputTokens = SummaryMaxTokens };

    /// <summary>One question to the model in a request of its own; its answer.</summary>
    /// <param name="prompt">The question</param>
    /// <param name="maxTokens">The longest answer; 0 for the summaries' size</param>
    /// <param name="cancellationToken">Cancels it</param>
    public async Task<string> AskOnceAsync(string prompt, int maxTokens, CancellationToken cancellationToken)
    {
        using IChatClient client = CreateChatClient();
        ChatOptions options = SummaryOptions;
        if (maxTokens > 0)
            options.MaxOutputTokens = maxTokens;
        ChatResponse response = await client.GetResponseAsync(prompt, options, cancellationToken);
        string text = (response.Text ?? string.Empty).Trim();
        if (text.Length == 0)
            throw new Exception("The answer is empty");
        return text;
    }
}
