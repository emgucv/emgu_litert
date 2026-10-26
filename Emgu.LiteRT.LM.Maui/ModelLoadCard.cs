//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System.ComponentModel;
using Emgu.LiteRT.LM.Chat;
using ModelLoadStage = Emgu.LiteRT.LM.Models.ModelLoadStage;

namespace Emgu.LiteRT.LM.Maui
{
    /// <summary>
    /// Shows a model being loaded (<see cref="ModelLoadState"/>): before a download, its size, a note when the device
    /// isn't on Wi-Fi and a Download button; then the stage, the download's progress and Cancel; an error with Retry. It
    /// hides itself once the model is ready.
    /// <para>
    /// Its look is the "ModelLoadCardTemplate" template (see <see cref="ChatStyles"/>), with the parts "PART_Title",
    /// "PART_Status" and "PART_Note" (Labels), "PART_Progress" (a ProgressBar), and "PART_Download" (reads "Continue" for
    /// a paused download, "Load" for a downloaded model), "PART_Cancel", "PART_Retry" and "PART_Dismiss" ("Not now": the
    /// card hides until loading is asked for again) - Buttons, or other views that are tapped. A template without one of
    /// them just doesn't show that part.
    /// </para>
    /// </summary>
    public class ModelLoadCard : TemplatedView
    {
        private const string TemplateKey = "ModelLoadCardTemplate";

        /// <summary>The loading to show.</summary>
        public static readonly BindableProperty StateProperty = BindableProperty.Create(
            nameof(State), typeof(ModelLoadState), typeof(ModelLoadCard), null, propertyChanged: OnStateChanged);

        private Label _title;
        private Label _status;
        private Label _note;
        private ProgressBar _progress;
        private View _download;
        private View _cancel;
        private View _retry;
        private View _dismiss;

        /// <summary>The loading to show.</summary>
        public ModelLoadState State
        {
            get => (ModelLoadState)GetValue(StateProperty);
            set => SetValue(StateProperty, value);
        }

        /// <summary>Create the card; it is hidden until it has a <see cref="State"/>.</summary>
        public ModelLoadCard()
        {
            IsVisible = false;
        }

        /// <inheritdoc/>
        protected override void OnParentSet()
        {
            base.OnParentSet();
            ChatResources.ApplyDefaultTemplate(this, TemplateKey);
        }

        /// <inheritdoc/>
        protected override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            _title = GetTemplateChild("PART_Title") as Label;
            _status = GetTemplateChild("PART_Status") as Label;
            _note = GetTemplateChild("PART_Note") as Label;
            _progress = GetTemplateChild("PART_Progress") as ProgressBar;
            _download = Hook("PART_Download", () => _ = State?.LoadAsync());
            _cancel = Hook("PART_Cancel", () => State?.Cancel());
            _retry = Hook("PART_Retry", () => _ = State?.LoadAsync());
            _dismiss = Hook("PART_Dismiss", () => { if (State != null) State.Dismissed = true; });
            Update();
        }

        private View Hook(string name, Action action)
        {
            View view = GetTemplateChild(name) as View;
            if (view is Button button)
                button.Clicked += (s, e) => action();
            else if (view != null)
            {
                TapGestureRecognizer tap = new TapGestureRecognizer();
                tap.Tapped += (s, e) => action();
                view.GestureRecognizers.Add(tap);
            }
            return view;
        }

        private static void OnStateChanged(BindableObject bindable, object oldValue, object newValue)
        {
            ModelLoadCard card = (ModelLoadCard)bindable;
            if (oldValue is ModelLoadState oldState)
                oldState.PropertyChanged -= card.OnStatePropertyChanged;
            if (newValue is ModelLoadState newState)
                newState.PropertyChanged += card.OnStatePropertyChanged;
            card.Update();
        }

        private void OnStatePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            Update();
        }

        private void Update()
        {
            ModelLoadState state = State;
            IsVisible = state != null && !state.IsReady && !state.Dismissed;
            if (state == null || state.IsReady)
                return;
            string name = state.DisplayName ?? state.Model.GetType().Name;
            bool offerDownload = !state.IsLoading && !state.HasError && !state.IsDownloaded;
            string status;
            if (state.HasError)
                status = "Couldn't load the model: " + state.Error;
            else if (!state.IsLoading)
                status = state.IsDownloaded ? "Not loaded yet."
                    : state.IsPaused ? "The download was paused" + (state.DownloadSize > 0 ? " at " + FormatSize(state.ModelFile.PartialDownloadBytes) + " of " + FormatSize(state.DownloadSize) + "." : ".")
                    : "The model needs to be downloaded first" + (state.DownloadSize > 0 ? " (" + FormatSize(state.DownloadSize) + ")." : ".");
            else
            {
                switch (state.Stage)
                {
                    case ModelLoadStage.Downloading:
                        status = state.Progress != null
                            ? string.Format("Downloading... {0:P0} ({1} of {2})", state.Progress, FormatSize(state.BytesReceived), FormatSize(state.TotalBytes ?? 0))
                            : "Downloading... " + FormatSize(state.BytesReceived);
                        break;
                    case ModelLoadStage.VerifyingFile:
                        status = "Checking the download...";
                        break;
                    case ModelLoadStage.LoadingEngine:
                        status = "Loading the model...";
                        break;
                    default:
                        status = "Checking the model file...";
                        break;
                }
            }
            if (_title != null)
                _title.Text = name;
            if (_status != null)
                _status.Text = status;
            if (_progress != null)
            {
                _progress.IsVisible = state.IsDownloading;
                _progress.Progress = state.Progress ?? 0;
            }
            if (_note != null)
            {
                // Before a download: the app's hint (e.g. how the download goes on in the background) and the Wi-Fi warning.
                string note = offerDownload ? string.Join(" ", new[] { state.Hint, OnWiFi() ? null : "You're not on Wi-Fi: the download may use mobile data." }
                    .Where(s => !string.IsNullOrEmpty(s))) : string.Empty;
                _note.IsVisible = note.Length > 0;
                _note.Text = note;
            }
            if (_download != null)
            {
                _download.IsVisible = offerDownload || (!state.IsLoading && !state.HasError && state.IsDownloaded);
                if (_download is Button downloadButton)
                    downloadButton.Text = state.IsDownloaded ? "Load" : state.IsPaused ? "Continue" : "Download";
            }
            if (_cancel != null)
                _cancel.IsVisible = state.IsLoading && state.Stage != ModelLoadStage.LoadingEngine;
            if (_retry != null)
            {
                _retry.IsVisible = state.HasError;
                if (_retry is Button retryButton)
                    retryButton.Text = state.HasError && state.IsPaused ? "Continue" : "Retry";
            }
            if (_dismiss != null)
                _dismiss.IsVisible = offerDownload;
        }

        private static bool OnWiFi()
        {
            try
            {
                IEnumerable<ConnectionProfile> profiles = Connectivity.Current.ConnectionProfiles;
                return profiles.Contains(ConnectionProfile.WiFi) || profiles.Contains(ConnectionProfile.Ethernet);
            }
            catch (Exception)
            {
                return true;
            }
        }

        private static string FormatSize(long bytes)
        {
            if (bytes >= 1L << 30)
                return string.Format("{0:0.0} GB", bytes / (double)(1L << 30));
            if (bytes >= 1L << 20)
                return string.Format("{0:0} MB", bytes / (double)(1L << 20));
            return string.Format("{0:0} KB", bytes / 1024.0);
        }
    }
}
