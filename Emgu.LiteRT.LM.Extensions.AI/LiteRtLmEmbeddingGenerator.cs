//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Models;
using Microsoft.Extensions.AI;

namespace Emgu.LiteRT.LM.Extensions.AI
{
    /// <summary>
    /// Microsoft.Extensions.AI's <see cref="IEmbeddingGenerator{TInput, TEmbedding}"/> over an <see cref="EmbeddingModel"/>
    /// (EmbeddingGemma...): texts as they are, or as search queries or documents with the model's prefixes when the
    /// options' <see cref="EmbeddingGenerationOptions.AdditionalProperties"/> carry <see cref="InputTypeKey"/> ("query" or
    /// "document"); <see cref="EmbeddingGenerationOptions.Dimensions"/> is the model's output size.
    /// </summary>
    public sealed class LiteRtLmEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        /// <summary>The additional property that says what the texts are: "query" or "document" (else plain texts).</summary>
        public const string InputTypeKey = "input_type";

        private readonly Func<CancellationToken, Task<EmbeddingModel>> _model;
        private readonly Func<CancellationToken, Task<IDisposable>> _enter;
        private readonly EmbeddingGeneratorMetadata _metadata;

        /// <summary>Embed with a loaded model.</summary>
        /// <param name="model">The model (initialized); the generator doesn't dispose it</param>
        /// <param name="modelId">The model's id in the embeddings' metadata, or null</param>
        public LiteRtLmEmbeddingGenerator(EmbeddingModel model, string modelId = null)
            : this(Loaded(model), modelId)
        {
        }

        private static Func<CancellationToken, Task<EmbeddingModel>> Loaded(EmbeddingModel model)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));
            Task<EmbeddingModel> loaded = Task.FromResult(model);
            return _ => loaded;
        }

        /// <summary>Embed with a model given for each request (loaded on first use, shared with other searches).</summary>
        /// <param name="model">Gives the loaded model</param>
        /// <param name="modelId">The model's id in the embeddings' metadata, or null</param>
        /// <param name="enter">Takes the model for one request (the model embeds one request at a time); disposing the result
        /// gives it back; or null when nothing else uses the model</param>
        public LiteRtLmEmbeddingGenerator(Func<CancellationToken, Task<EmbeddingModel>> model, string modelId = null,
            Func<CancellationToken, Task<IDisposable>> enter = null)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _enter = enter;
            _metadata = new EmbeddingGeneratorMetadata("emgu-litert", null, modelId);
        }

        /// <inheritdoc/>
        public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values,
            EmbeddingGenerationOptions options = null, CancellationToken cancellationToken = default)
        {
            if (values == null)
                throw new ArgumentNullException(nameof(values));
            string[] texts = values.ToArray();
            int size = options?.Dimensions ?? 0;
            string type = InputType(options);
            EmbeddingModel model = await _model(cancellationToken).ConfigureAwait(false);
            float[][] vectors;
            using (_enter == null ? null : await _enter(cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                vectors = await Task.Run(() =>
                    type == "query" ? texts.Select(text => model.EmbedQuery(text, size)).ToArray()
                    : type == "document" ? model.EmbedDocuments(texts, size)
                    : model.Embed(texts, size), cancellationToken).ConfigureAwait(false);
            }
            DateTimeOffset created = DateTimeOffset.UtcNow;
            return new GeneratedEmbeddings<Embedding<float>>(vectors.Select(vector =>
                new Embedding<float>(vector) { ModelId = _metadata.DefaultModelId, CreatedAt = created }));
        }

        private static string InputType(EmbeddingGenerationOptions options)
        {
            if (options?.AdditionalProperties == null || !options.AdditionalProperties.TryGetValue(InputTypeKey, out object value) || value == null)
                return null;
            string type = value.ToString();
            if (type != "query" && type != "document")
                throw new ArgumentException("The input type is \"query\" or \"document\", not \"" + type + "\".", nameof(options));
            return type;
        }

        /// <inheritdoc/>
        public object GetService(Type serviceType, object serviceKey = null)
        {
            if (serviceType == null)
                throw new ArgumentNullException(nameof(serviceType));
            if (serviceKey != null)
                return null;
            if (serviceType == typeof(EmbeddingGeneratorMetadata))
                return _metadata;
            return serviceType.IsInstanceOfType(this) ? this : null;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
        }
    }
}
