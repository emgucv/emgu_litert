//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.LiteRT.LM.Chat;
using LanguageModel = Emgu.LiteRT.LM.Models.LanguageModel;

namespace Emgu.LiteRT.LM.Maui
{
    // A scheduled task's run (ScheduledTaskRunner) while this page has its model loaded: the page lends the model rather than
    // the run loading a second engine of it, and is busy meanwhile ("Running a scheduled task..."). The run's conversation is
    // its own, so the page's next message reads the chat again (LiteRtLmChatClient starts a new conversation).
    public partial class ChatPage
    {
        // On the main thread. Null: no model to lend; ModelLoan.Busy: replying, loading or private - the run waits.
        internal ModelLoan LendModelForTask()
        {
            if (_model == null)
                return null;
            if (IsWorking || IsPrivate || _chatView?.LoadState?.IsLoading == true)
                return ModelLoan.Busy;
            if (!ModelReady)
                return null;
            ChatModelOption option = _model;
            LanguageModel model = _modelInstance;
            SetBusy(true);
            SetStatus("Running a scheduled task...");
            return new ModelLoan(_client, option, model, option.IsRemote ? "remote" : _backend,
                conversation => ConfigureConversation(conversation, option, model),
                () => MainThread.BeginInvokeOnMainThread(() =>
                {
                    SetBusy(false);
                    SetStatus(null);
                }));
        }

        // A saved chat a notification asked for (ChatLaunch, a scheduled task's answer): opened when the page appears, or at
        // once while it shows - after a reply or a model's load in progress (OpenChatAsync does nothing meanwhile).
        private async void OpenLaunchedChat()
        {
            if (!ChatLaunch.TryTake(out int chatId))
                return;
            for (int wait = 0; wait < 60 && IsWorking; wait++)
                await Task.Delay(500);
            try { await OpenChatAsync(chatId); }
            catch (Exception) { }
        }

        private void OnOpenChatRequested(int chatId) => OpenLaunchedChat();
    }
}
