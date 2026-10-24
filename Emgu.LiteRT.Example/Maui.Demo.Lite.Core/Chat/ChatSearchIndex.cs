// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Models;

namespace Maui.Demo.Lite;

// SQLite owns the index; no stale in-memory copy survives deletions or a change of embedding model.
internal sealed class ChatSearchIndex
{
    private readonly ChatStore _store;
    public ChatSearchIndex(ChatStore store) { _store = store; }

    public async Task IndexAsync(string model, Func<string, CancellationToken, Task<float[]>> embed,
        Action<int, int> progress, CancellationToken cancel)
    {
        List<MessageRecord> messages = await _store.GetSearchMessagesAsync();
        var indexed = (await _store.GetChatEmbeddingsAsync()).Where(e => e.Model == model)
            .Select(e => e.MessageId).ToHashSet();
        var pending = messages.Where(m => !indexed.Contains(m.Id)).ToList();
        int done = messages.Count - pending.Count;
        progress?.Invoke(done, messages.Count);
        foreach (MessageRecord message in pending)
        {
            var passages = new List<ChatEmbeddingRecord>();
            int position = 0;
            foreach (string text in ChatSearch.Chunk(message.Text))
            {
                cancel.ThrowIfCancellationRequested();
                float[] vector = await embed(text, cancel);
                var bytes = new byte[vector.Length * sizeof(float)];
                Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
                passages.Add(new ChatEmbeddingRecord { Id = message.Id + "_" + position,
                    MessageId = message.Id, Position = position++, Model = model, Text = text, Embedding = bytes });
            }
            cancel.ThrowIfCancellationRequested();
            await _store.SaveChatEmbeddingsAsync(message.Id, passages);
            progress?.Invoke(++done, messages.Count);
        }
    }

    public async Task<List<ChatSearchRecord>> SearchAsync(string model, PhotoQuery query, int? currentConversation,
        Func<string, CancellationToken, Task<float[]>> embed, CancellationToken cancel)
    {
        var candidates = (await _store.GetChatEmbeddingsAsync()).Where(e => e.Model == model
            && e.ConversationId != currentConversation
            && (!query.From.HasValue || LocalDate(e.Created) >= query.From.Value)
            && (!query.To.HasValue || LocalDate(e.Created) <= query.To.Value)).ToList();
        cancel.ThrowIfCancellationRequested();
        if (candidates.Count == 0) return new List<ChatSearchRecord>();
        IEnumerable<ChatSearchRecord> matches;
        if (string.IsNullOrWhiteSpace(query.Description))
            matches = candidates.OrderByDescending(e => e.Created).ThenBy(e => e.Position);
        else
        {
            float[] vector = await embed(query.Description, cancel);
            var scored = candidates.Select(e => (Entry: e, Score: EmbeddingModel.CosineSimilarity(vector, ToFloats(e.Embedding))))
                .OrderByDescending(e => e.Score).ToList();
            float best = scored[0].Score;
            // Conservative absolute and relative cutoffs; return evidence, not an assertion that it is the answer.
            matches = scored.Where(e => e.Score >= 0.5f && e.Score >= best - 0.12f).Select(e => e.Entry);
        }
        cancel.ThrowIfCancellationRequested();
        var found = matches.GroupBy(e => e.MessageId).Select(g => g.First()).Take(5).ToList();
        var existing = (await _store.GetConversationsAsync()).Select(c => c.Id).ToHashSet();
        found.RemoveAll(e => !existing.Contains(e.ConversationId));
        return found;
    }

    private static DateTime LocalDate(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc).ToLocalTime();
    private static float[] ToFloats(byte[] bytes)
    {
        var result = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, result, 0, bytes.Length);
        return result;
    }
}
