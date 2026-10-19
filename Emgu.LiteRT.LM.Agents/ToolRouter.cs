//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace Emgu.LiteRT.LM.Agents
{
    /// <summary>
    /// A set of tools for one kind of task - the camera, device actions, goals... - that an agent gets only when a
    /// message needs it (ToolRouter), with the instructions that go with them.
    /// </summary>
    public sealed class ToolGroup
    {
        /// <summary>
        /// Create a tool group
        /// </summary>
        /// <param name="name">A short name, e.g. "camera"; the stub tool is called use_{name}</param>
        /// <param name="summary">What the tools do, for the stub's description, e.g. "look at, watch for or photograph
        /// something with the device's camera"</param>
        /// <param name="keywords">Words and phrases of a message that need the group (lower case; a keyword also
        /// matches longer words it starts, e.g. "remind" matches "reminder")</param>
        /// <param name="createTools">Creates the group's tools (none: the group is left out, e.g. when a setting is off)</param>
        /// <param name="instructions">Gives the instructions for the agent while the group is in use, or null</param>
        /// <param name="alsoMatches">Another test of whether a message needs the group, e.g. the user's own goals'
        /// words, or null</param>
        /// <param name="rerunAfterStub">Whether its stub ends the request so the message is sent again with the group
        /// (ToolRouter's endRequest) rather than continuing it</param>
        public ToolGroup(string name, string summary, IEnumerable<string> keywords, Func<IEnumerable<AITool>> createTools,
            Func<string> instructions = null, Func<string, bool> alsoMatches = null, bool rerunAfterStub = false)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("A name is needed", nameof(name));
            Name = name;
            Summary = summary ?? string.Empty;
            Keywords = (keywords ?? Enumerable.Empty<string>()).Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim().ToLowerInvariant()).ToList();
            CreateTools = createTools ?? throw new ArgumentNullException(nameof(createTools));
            Instructions = instructions;
            AlsoMatches = alsoMatches;
            RerunAfterStub = rerunAfterStub;
        }

        /// <summary>Whether its stub ends the request so the message is sent again with the group</summary>
        public bool RerunAfterStub { get; }

        /// <summary>The group's name, e.g. "camera"</summary>
        public string Name { get; }

        /// <summary>What the tools do</summary>
        public string Summary { get; }

        /// <summary>Words and phrases of a message that need the group</summary>
        public IReadOnlyList<string> Keywords { get; }

        /// <summary>Creates the group's tools</summary>
        public Func<IEnumerable<AITool>> CreateTools { get; }

        /// <summary>Gives the instructions for the agent while the group is in use, or null</summary>
        public Func<string> Instructions { get; }

        /// <summary>Another test of whether a message needs the group, or null</summary>
        public Func<string, bool> AlsoMatches { get; }

        /// <summary>The name of the group's stub tool: use_{Name}</summary>
        public string StubName => "use_" + Name;
    }

    /// <summary>
    /// Gives an agent only the tools a conversation needs, so small context windows aren't filled by tool definitions
    /// (Gemma 4 E2B on a phone's 4k window failed every message once the demo had ~25 tools). The app adds its core tools
    /// itself; the router adds the groups a message needs - from its words (Route) or the app's state (Activate) - and
    /// keeps them for the rest of the conversation, since a model like LiteRT-LM's re-reads the whole conversation
    /// when its tools change. Every other group gets a stub tool (use_camera...) of a line or two: a model that calls
    /// one gets the group's tools and instructions in the same request (through the options the function invocation
    /// uses, e.g. FunctionInvokingChatClient.CurrentContext.Options) and can call them straight away, so a request the
    /// keywords miss still works in one message.
    /// </summary>
    public sealed class ToolRouter
    {
        private readonly List<ToolGroup> _groups;
        private readonly HashSet<string> _active = new HashSet<string>(StringComparer.Ordinal);
        private readonly Func<ChatOptions> _currentOptions;
        private readonly Action _endRequest;
        private bool _rerun;

        /// <summary>
        /// Create a router.
        /// </summary>
        /// <param name="groups">The groups</param>
        /// <param name="currentOptions">The options of the request a tool is being called in, e.g.
        /// () => FunctionInvokingChatClient.CurrentContext?.Options; a stub adds its group's tools there. Without it (or
        /// if it returns null) the group is used from the next message on.</param>
        /// <param name="endRequest">Ends the request (e.g. () => FunctionInvokingChatClient.CurrentContext.Terminate =
        /// true): the stubs of groups with RerunAfterStub call it instead of adding their tools, and TakeRerun tells the
        /// app to send the message again - without the stub's turn, with the group's tools from the start. Which is better
        /// depends on the group: continuing after the stub, Gemma 4 E2B asked for details it had been given ("Add the
        /// dentist on Friday at 3 pm to my calendar": the date, the title), and sent again it added the event; but sent
        /// again with the camera tools, it asked which object class "a bird" was, where continuing it set the monitor
        /// up.</param>
        public ToolRouter(IEnumerable<ToolGroup> groups, Func<ChatOptions> currentOptions, Action endRequest = null)
        {
            _groups = (groups ?? throw new ArgumentNullException(nameof(groups))).ToList();
            _currentOptions = currentOptions;
            _endRequest = endRequest;
        }

        /// <summary>
        /// Whether a stub ended the last request (with endRequest), so the app should send the message again; resets it.
        /// </summary>
        public bool TakeRerun()
        {
            bool rerun = _rerun;
            _rerun = false;
            return rerun;
        }

        /// <summary>The groups</summary>
        public IReadOnlyList<ToolGroup> Groups => _groups;

        /// <summary>The names of the groups in use in this conversation</summary>
        public IReadOnlyCollection<string> Active => _active;

        /// <summary>Raised when a stub tool adds its group, with the group's name</summary>
        public event Action<string> GroupRequested;

        /// <summary>Whether a group is in use in this conversation</summary>
        /// <param name="name">The group's name</param>
        public bool IsActive(string name) => _active.Contains(name);

        /// <summary>Start a new conversation: no group is in use.</summary>
        public void Reset()
        {
            _active.Clear();
            _rerun = false;
        }

        /// <summary>
        /// Use a group from now on, e.g. the camera group while the live camera is open.
        /// </summary>
        /// <param name="name">The group's name</param>
        /// <returns>True if it wasn't in use yet</returns>
        public bool Activate(string name)
        {
            if (!_groups.Any(g => g.Name == name))
                return false;
            return _active.Add(name);
        }

        /// <summary>
        /// The groups a message needs by its words, which are used from now on.
        /// </summary>
        /// <param name="message">The user's message</param>
        /// <returns>The names of the groups it added</returns>
        public IList<string> Route(string message)
        {
            List<string> added = new List<string>();
            foreach (ToolGroup group in _groups)
                if (!_active.Contains(group.Name) && Matches(group, message))
                {
                    _active.Add(group.Name);
                    added.Add(group.Name);
                }
            return added;
        }

        /// <summary>
        /// The groups whose keywords are in a text (without using them).
        /// </summary>
        /// <param name="text">The text</param>
        /// <returns>The names of the groups</returns>
        public IList<string> Match(string text) => _groups.Where(g => Matches(g, text)).Select(g => g.Name).ToList();

        private static bool Matches(ToolGroup group, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;
            if (group.AlsoMatches != null && group.AlsoMatches(text))
                return true;
            string normalized = " " + Normalize(text) + " ";
            foreach (string keyword in group.Keywords)
            {
                // At the start of a word; the end may run on ("remind" - "reminder", "photo" - "photos").
                if (normalized.IndexOf(" " + Normalize(keyword), StringComparison.Ordinal) >= 0)
                    return true;
            }
            return false;
        }

        private static string Normalize(string text) =>
            Regex.Replace(text.ToLowerInvariant().Replace('’', '\''), @"[^\p{L}\p{N}']+", " ").Trim();

        /// <summary>
        /// The tools for a request: the tools of the groups in use, and a stub for each other group.
        /// </summary>
        /// <returns>The tools</returns>
        public IList<AITool> Tools()
        {
            List<AITool> tools = new List<AITool>();
            foreach (ToolGroup group in _groups)
            {
                List<AITool> groupTools = group.CreateTools().ToList();
                if (groupTools.Count == 0)
                    continue;
                if (_active.Contains(group.Name))
                    tools.AddRange(groupTools);
                else
                    tools.Add(Stub(group));
            }
            return tools;
        }

        /// <summary>
        /// The instructions of the groups in use, and a line on the stubs of the others, or null.
        /// </summary>
        public string Instructions()
        {
            List<string> parts = _groups.Where(g => _active.Contains(g.Name) && g.Instructions != null)
                .Select(g => g.Instructions()).Where(t => !string.IsNullOrEmpty(t)).ToList();
            // A stub alone wasn't enough: given "Beep when a bird lands on the feeder" and use_camera, Gemma 4 E2B said
            // it couldn't set up alarms.
            List<ToolGroup> stubs = _groups.Where(g => !_active.Contains(g.Name) && g.CreateTools().Any()).ToList();
            if (stubs.Count > 0)
                parts.Add("More tools are a call away: " + string.Join("; ", stubs.Select(g => g.StubName + " to " + g.Summary)) +
                    ". When the user asks for one of these, call its use_ tool first, then the tools it gives you - never say you can't do it.");
            return parts.Count == 0 ? null : string.Join("\n\n", parts);
        }

        private AIFunction Stub(ToolGroup group)
        {
            return AIFunctionFactory.Create(() => UseGroup(group), group.StubName,
                "Call this first to " + group.Summary + ": it gives you the tools for that, which you then call to do it.");
        }

        private string UseGroup(ToolGroup group)
        {
            _active.Add(group.Name);
            GroupRequested?.Invoke(group.Name);
            if (_endRequest != null && group.RerunAfterStub)
            {
                _rerun = true;
                _endRequest();
                return "The tools are ready.";
            }
            ChatOptions options = _currentOptions?.Invoke();
            List<AITool> tools = group.CreateTools().ToList();
            if (options == null)
                return "The tools to " + group.Summary + " can be used from the user's next message: ask them to say it again.";
            if (options.Tools == null)
                options.Tools = new List<AITool>();
            for (int i = options.Tools.Count - 1; i >= 0; i--)
                if (options.Tools[i].Name == group.StubName)
                    options.Tools.RemoveAt(i);
            foreach (AITool tool in tools)
                options.Tools.Add(tool);
            string instructions = group.Instructions?.Invoke();
            if (!string.IsNullOrEmpty(instructions))
                options.Instructions = string.IsNullOrEmpty(options.Instructions) ? instructions : options.Instructions + "\n\n" + instructions;
            // The group's instructions go in the result too: added to the system prompt alone, Gemma 4 E2B called
            // use_clock_calendar_maps for "Add the dentist on Friday at 3 pm to my calendar" and then asked for the date
            // instead of passing "Friday at 3 pm" (routed by its words, the same model added the event).
            return "You can use these tools now: " + string.Join(", ", tools.Select(t => t.Name)) + "." +
                (string.IsNullOrEmpty(instructions) ? "" : " " + instructions) +
                " Do it now: call the right one with what the user said, in their words - don't ask for details they already gave, or for ones the tool doesn't need.";
        }
    }
}
