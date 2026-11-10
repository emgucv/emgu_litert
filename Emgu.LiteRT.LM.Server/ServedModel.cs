//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using Microsoft.Extensions.AI;

namespace Emgu.LiteRT.LM.Server
{
    /// <summary>
    /// The model a <see cref="ChatServer"/> serves, as GET /v1/models describes it: its id and name, its context window and
    /// what it accepts - so a client sets itself up (Emgu.LiteRT.LM.Maui.OpenAI's ServerFeature reads them).
    /// </summary>
    public sealed class ServedModel
    {
        /// <summary>Describe a model.</summary>
        /// <param name="id">The id clients ask for, e.g. "gemma-4-e4b"</param>
        /// <param name="displayName">Its name for people, e.g. "Gemma 4 E4B"</param>
        public ServedModel(string id, string displayName = null)
        {
            if (string.IsNullOrEmpty(id))
                throw new ArgumentException("An id is needed", nameof(id));
            Id = id;
            DisplayName = string.IsNullOrEmpty(displayName) ? id : displayName;
        }

        /// <summary>The id clients ask for.</summary>
        public string Id { get; }

        /// <summary>Its name for people.</summary>
        public string DisplayName { get; }

        /// <summary>The context window in tokens, or 0 if unknown.</summary>
        public int ContextLength { get; set; }

        /// <summary>Whether it accepts images.</summary>
        public bool Vision { get; set; }

        /// <summary>Whether it accepts audio.</summary>
        public bool Audio { get; set; }

        /// <summary>Whether it calls tools.</summary>
        public bool Tools { get; set; }

        /// <summary>Whether it can think before answering.</summary>
        public bool Thinking { get; set; }

        /// <summary>An embedding model's vectors' length, or 0 if unknown (or a chat model).</summary>
        public int Dimensions { get; set; }
    }

    /// <summary>
    /// What a <see cref="ChatServer"/> serves right now: a client and its model. The host gives one for each request, or null
    /// when no model is ready (the request gets 503).
    /// </summary>
    public sealed class ServedChat
    {
        /// <summary>Serve a client.</summary>
        /// <param name="client">The model's client, without function invocation: the server returns the model's tool calls to
        /// the phone, which runs them</param>
        /// <param name="model">The model</param>
        /// <param name="beginRequest">Called as a request starts, with the device's name; disposing its result marks the request
        /// done (e.g. the host shows "Serving ..."); or null</param>
        public ServedChat(IChatClient client, ServedModel model, Func<string, IDisposable> beginRequest = null)
        {
            Client = client ?? throw new ArgumentNullException(nameof(client));
            Model = model ?? throw new ArgumentNullException(nameof(model));
            BeginRequest = beginRequest;
        }

        /// <summary>The model's client.</summary>
        public IChatClient Client { get; }

        /// <summary>The model.</summary>
        public ServedModel Model { get; }

        /// <summary>Called as a request starts, or null.</summary>
        public Func<string, IDisposable> BeginRequest { get; }
    }

    /// <summary>
    /// The search (embedding) model a <see cref="ChatServer"/> serves at POST /v1/embeddings: a generator and its model. The
    /// host gives one for each request, or null when there is none (the request gets 503).
    /// </summary>
    public sealed class ServedEmbeddings
    {
        /// <summary>Serve an embedding generator.</summary>
        /// <param name="generator">The model's generator; a request's "input_type" ("query" or "document") reaches it as
        /// the options' additional property "input_type", its "dimensions" as the options' Dimensions</param>
        /// <param name="model">The model</param>
        /// <param name="beginRequest">Called as a request starts, with the device's name; disposing its result marks the request
        /// done; or null</param>
        public ServedEmbeddings(IEmbeddingGenerator<string, Embedding<float>> generator, ServedModel model, Func<string, IDisposable> beginRequest = null)
        {
            Generator = generator ?? throw new ArgumentNullException(nameof(generator));
            Model = model ?? throw new ArgumentNullException(nameof(model));
            BeginRequest = beginRequest;
        }

        /// <summary>The model's generator.</summary>
        public IEmbeddingGenerator<string, Embedding<float>> Generator { get; }

        /// <summary>The model.</summary>
        public ServedModel Model { get; }

        /// <summary>Called as a request starts, or null.</summary>
        public Func<string, IDisposable> BeginRequest { get; }
    }
}
