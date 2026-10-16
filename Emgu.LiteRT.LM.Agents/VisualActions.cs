// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Emgu.LiteRT.LM.Agents.Events
{
    /// <summary>Discoverable host action. ParameterSchema is a JSON Schema for the parameters object.</summary>
    public sealed class VisualActionDescriptor
    {
        /// <summary>Stable action identifier.</summary>
        public string Id { get; }
        /// <summary>Human-readable name.</summary>
        public string Name { get; }
        /// <summary>Explains the action to a planner.</summary>
        public string Description { get; }
        /// <summary>Whether this action can be bound on the current host.</summary>
        public bool Available { get; }
        /// <summary>Owned JSON Schema constraining the parameters object.</summary>
        public JsonElement ParameterSchema { get; }
        /// <summary>Creates a descriptor, copying the schema out of its JSON document.</summary>
        public VisualActionDescriptor(string id, string name, string description, JsonElement parameterSchema, bool available = true)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("An action ID is required.");
            if (parameterSchema.ValueKind != JsonValueKind.Object) throw new ArgumentException("An object parameter schema is required.");
            Id = id; Name = name; Description = description; Available = available; ParameterSchema = parameterSchema.Clone();
        }
    }

    /// <summary>Untrusted action configuration received from a tool or host. Factories validate it before binding.</summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class VisualActionRequest
    {
        /// <summary>Registered action ID.</summary>
        [JsonPropertyName("actionId")]
        public string ActionId { get; set; }
        /// <summary>Parameters object; unknown fields and invalid values must be rejected.</summary>
        [JsonPropertyName("parameters")]
        public JsonElement Parameters { get; set; }
        /// <summary>Used by JSON deserializers.</summary>
        public VisualActionRequest() { }
        /// <summary>Copies parameters for a programmatic request.</summary>
        public VisualActionRequest(string actionId, JsonElement parameters)
        { ActionId = actionId; Parameters = parameters.Clone(); }
    }

    /// <summary>A validated immutable binding. The factory owns normalization and per-rule action state.</summary>
    public sealed class VisualActionBinding
    {
        /// <summary>Action implementation; binding does not execute it.</summary>
        public IVisualAction Action { get; }
        /// <summary>Owned, validated and normalized parameters.</summary>
        public JsonElement Parameters { get; }
        /// <summary>Creates a binding, copying its normalized parameter object.</summary>
        public VisualActionBinding(IVisualAction action, JsonElement parameters)
        {
            Action = action ?? throw new ArgumentNullException(nameof(action));
            if (parameters.ValueKind != JsonValueKind.Object) throw new ArgumentException("Action parameters must be an object.");
            Parameters = parameters.Clone();
        }
    }

    /// <summary>Validates all parameters and creates a bound action without performing side effects.</summary>
    public interface IVisualActionFactory
    {
        /// <summary>Current capabilities and parameter schema.</summary>
        VisualActionDescriptor Descriptor { get; }
        /// <summary>Reject invalid/unknown fields; return immutable normalized configuration and a bound action.</summary>
        VisualActionBinding Bind(JsonElement parameters);
    }

    /// <summary>Optional host reservation around an event's concurrent actions, for example shared audio ownership.</summary>
    public interface IVisualActionGroupCoordinator
    {
        /// <summary>Reserve resources before launching actions; dispose the lease after all actions finish cleanup.</summary>
        Task<IDisposable> EnterAsync(VisualEvent occurrence, CancellationToken cancellationToken);
    }

    /// <summary>State of one action in the latest dispatched event group.</summary>
#if NET8_0_OR_GREATER
    [JsonConverter(typeof(JsonStringEnumConverter<VisualActionState>))]
#else
    [JsonConverter(typeof(JsonStringEnumConverter))]
#endif
    public enum VisualActionState
    {
        /// <summary>Waiting for this event group to execute.</summary>
        Queued,
        /// <summary>Execution has started.</summary>
        Running,
        /// <summary>Completed successfully.</summary>
        Succeeded,
        /// <summary>Failed without cancelling sibling actions.</summary>
        Failed,
        /// <summary>Cancelled by visibility or rule changes.</summary>
        Cancelled,
        /// <summary>The bounded dispatcher could not accept this event.</summary>
        Skipped
    }

    /// <summary>Immutable snapshot of a rule's bound action.</summary>
    public sealed class VisualActionInfo
    {
        /// <summary>Registered action ID.</summary>
        public string ActionId { get; }
        /// <summary>Human-readable action name.</summary>
        public string Name { get; }
        /// <summary>Validated normalized parameters.</summary>
        public JsonElement Parameters { get; }
        internal VisualActionInfo(string actionId, string name, JsonElement parameters)
        { ActionId = actionId; Name = name; Parameters = parameters; }
    }

    /// <summary>Immutable per-action outcome for one event episode.</summary>
    public sealed class VisualActionResult
    {
        /// <summary>Registered action ID.</summary>
        public string ActionId { get; }
        /// <summary>Presence episode that scheduled this action.</summary>
        public Guid EpisodeId { get; }
        /// <summary>Current execution state.</summary>
        public VisualActionState State { get; }
        /// <summary>Completion/error/cancellation detail.</summary>
        public string Message { get; }
        internal VisualActionResult(string actionId, Guid episodeId, VisualActionState state, string message)
        { ActionId = actionId; EpisodeId = episodeId; State = state; Message = message; }
    }

    /// <summary>Parameter helpers for action factories. No dynamic code is executed.</summary>
    public static class VisualActionParameters
    {
        /// <summary>An owned empty parameters object.</summary>
        public static JsonElement Empty { get; } = Parse("{}");
        /// <summary>Copies a JSON value from a temporary document.</summary>
        public static JsonElement Parse(string json) { using (var doc = JsonDocument.Parse(json)) return doc.RootElement.Clone(); }
        /// <summary>Requires an object containing only the named fields, with no duplicate JSON keys.</summary>
        public static void AllowOnly(JsonElement parameters, params string[] fields)
        {
            if (parameters.ValueKind != JsonValueKind.Object) throw new ArgumentException("Action parameters must be an object.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in parameters.EnumerateObject())
                if (!fields.Contains(p.Name) || !seen.Add(p.Name)) throw new ArgumentException("Unknown or repeated action parameter: " + p.Name);
        }
        /// <summary>Reads and trims required literal text, rejecting blank or overlong input.</summary>
        public static string RequiredText(JsonElement parameters, string field, int maxLength)
        {
            if (!parameters.TryGetProperty(field, out var value) || value.ValueKind != JsonValueKind.String)
                throw new ArgumentException("A string '" + field + "' parameter is required.");
            string text = value.GetString().Trim();
            if (text.Length == 0 || text.Length > maxLength) throw new ArgumentException(field + " must contain 1–" + maxLength + " characters.");
            return text;
        }
        /// <summary>Creates normalized single-text parameters without reflection serialization.</summary>
        public static JsonElement Text(string text)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new Utf8JsonWriter(stream)) { writer.WriteStartObject(); writer.WriteString("text", text); writer.WriteEndObject(); }
                return Parse(Encoding.UTF8.GetString(stream.ToArray()));
            }
        }
        internal static string Canonical(JsonElement value)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new Utf8JsonWriter(stream)) WriteCanonical(writer, value);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
        private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                writer.WriteStartObject();
                foreach (var p in value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                { writer.WritePropertyName(p.Name); WriteCanonical(writer, p.Value); }
                writer.WriteEndObject();
            }
            else if (value.ValueKind == JsonValueKind.Array)
            { writer.WriteStartArray(); foreach (var item in value.EnumerateArray()) WriteCanonical(writer, item); writer.WriteEndArray(); }
            else value.WriteTo(writer);
        }
    }

    /// <summary>Explicit action factories. Register before runtime/tool use; never resolves arbitrary code or names.</summary>
    public sealed class VisualActionRegistry
    {
        private readonly Dictionary<string, IVisualActionFactory> _factories = new Dictionary<string, IVisualActionFactory>(StringComparer.Ordinal);
        /// <summary>Registers one factory per stable ID.</summary>
        public void Register(IVisualActionFactory factory)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            _factories.Add(factory.Descriptor.Id, factory);
        }
        /// <summary>Snapshots descriptors, including unavailable actions.</summary>
        public IReadOnlyList<VisualActionDescriptor> Describe() => _factories.Values.Select(f => f.Descriptor).ToArray();
        internal VisualActionBinding[] Bind(IEnumerable<VisualActionRequest> requests)
        {
            if (requests == null) throw new ArgumentException("An explicit nonempty actions list is required.");
            var input = requests.ToArray();
            if (input.Length == 0) throw new ArgumentException("At least one action is required.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            // Validate every ID before invoking any factory; factories must remain side-effect-free.
            foreach (var r in input)
            {
                if (r == null || string.IsNullOrWhiteSpace(r.ActionId) || !_factories.TryGetValue(r.ActionId, out var factory) || !factory.Descriptor.Available)
                    throw new ArgumentException("Unavailable action. Use list_visual_actions for supported IDs.");
                if (!seen.Add(r.ActionId)) throw new ArgumentException("Use each action type at most once per rule.");
                if (r.Parameters.ValueKind != JsonValueKind.Object) throw new ArgumentException("Action parameters must be an object.");
            }
            return input.Select(r =>
            {
                var binding = _factories[r.ActionId].Bind(r.Parameters);
                if (binding == null || binding.Action.Id != r.ActionId) throw new InvalidOperationException("Action factory returned a mismatched binding.");
                return binding;
            }).ToArray();
        }
        internal string Name(string id) => _factories[id].Descriptor.Name;
        internal static string Key(IEnumerable<VisualActionBinding> bindings) => string.Join("\n", bindings.OrderBy(b => b.Action.Id, StringComparer.Ordinal)
            .Select(b => JsonSerializer.Serialize(b.Action.Id) + ":" + VisualActionParameters.Canonical(b.Parameters)));
        internal static VisualActionRegistry FromActions(IEnumerable<IVisualAction> actions)
        {
            var registry = new VisualActionRegistry();
            foreach (var action in actions) registry.Register(new FixedFactory(action));
            return registry;
        }
        private sealed class FixedFactory : IVisualActionFactory
        {
            private readonly IVisualAction _action;
            public VisualActionDescriptor Descriptor { get; }
            public FixedFactory(IVisualAction action)
            {
                _action = action;
                Descriptor = new VisualActionDescriptor(action.Id, action.Id, "Registered host action without parameters.",
                    VisualActionParameters.Parse("{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}"));
            }
            public VisualActionBinding Bind(JsonElement parameters)
            { VisualActionParameters.AllowOnly(parameters); return new VisualActionBinding(_action, VisualActionParameters.Empty); }
        }
    }
}
