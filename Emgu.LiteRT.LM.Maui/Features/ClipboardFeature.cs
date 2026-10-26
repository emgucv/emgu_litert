//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.LiteRT.LM.Agents;
using Microsoft.Extensions.AI;

namespace Emgu.LiteRT.LM.Maui.Features
{
    /// <summary>
    /// The clipboard: the read_clipboard tool (<see cref="AgentTools.ReadClipboard"/>), only with a message that asks
    /// about what the user copied (<see cref="AgentTools.MentionsClipboard"/>), since a clipboard can hold passwords.
    /// Android 12+ and iOS tell the user that the app read it. No settings.
    /// </summary>
    public sealed class ClipboardFeature : ChatFeature
    {
        /// <inheritdoc/>
        public override string Name => "clipboard";

        /// <inheritdoc/>
        public override string Title => "Clipboard";

        /// <inheritdoc/>
        public override bool PersistEnabled => false;

        /// <inheritdoc/>
        public override IEnumerable<AITool> Tools(ChatMessageContext context) =>
            AgentTools.MentionsClipboard(context.Prompt) ? new AITool[] { AgentTools.ReadClipboard(ReadClipboardAsync) } : Array.Empty<AITool>();

        /// <summary>The text on the clipboard, or null.</summary>
        /// <param name="cancellationToken">Cancels it</param>
        public static Task<string> ReadClipboardAsync(CancellationToken cancellationToken) =>
            MainThread.InvokeOnMainThreadAsync(async () => Clipboard.Default.HasText ? await Clipboard.Default.GetTextAsync() : null);
    }
}
