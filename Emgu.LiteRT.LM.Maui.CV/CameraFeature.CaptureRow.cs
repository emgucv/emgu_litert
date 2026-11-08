// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using Emgu.LiteRT.LM.Agents.Events;
using Emgu.LiteRT.LM.Chat;
using Emgu.LiteRT.LM.Maui;

namespace Emgu.LiteRT.LM.Maui.CV;

// One opening of the camera in the chat: when the camera closes, what it took is one row on the latest reply - a header with
// what and when, Keep all, and the thumbnails in a horizontal scroll, newest on the left - instead of a card per capture. A
// chat read back shows the reply's kept captures as one such row (RestoreSavedFiles).
public sealed partial class CameraFeature
{
    private const double CaptureRowThumbnailHeight = 96;

    private sealed class CaptureRow
    {
        private readonly CameraFeature _feature;
        private readonly List<CameraCapture> _items;
        private readonly ChatMessageItem _reply;
        private readonly bool _restored;
        private readonly HorizontalStackLayout _thumbnails = new() { Spacing = 6 };
        private readonly Label _summary;
        private readonly Button _keepAll;

        // A row of captures, newest first, on a reply (where Keep all saves them; null for a restored row, all kept already).
        public CaptureRow(CameraFeature feature, List<CameraCapture> items, ChatMessageItem reply, bool restored)
        {
            _feature = feature;
            _items = items;
            _reply = reply;
            _restored = restored;
            _summary = new Label { FontSize = 12, TextColor = CvViews.Color("ChatSecondaryText"), VerticalOptions = LayoutOptions.Center };
            _keepAll = MonitorButton("Keep all");
            SemanticProperties.SetDescription(_keepAll, "Save all of these with the chat, so they are here when the chat is opened again");
            _keepAll.Clicked += async (_, _) => await KeepAllAsync();
            foreach (CameraCapture capture in items)
            {
                if (capture.Tag is CaptureViews views)
                {
                    views.Row = this;
                    views.Reply = reply;
                }
                _thumbnails.Children.Add(feature.CaptureThumbnail(capture, CaptureRowThumbnailHeight, markKept: true));
            }
            var scroll = new ScrollView
            {
                Orientation = ScrollOrientation.Horizontal, HorizontalScrollBarVisibility = ScrollBarVisibility.Never, Content = _thumbnails
            };
            SemanticProperties.SetDescription(scroll, "Pictures and clips the camera took, newest first");
            View = new VerticalStackLayout
            {
                Spacing = 4, Margin = new Thickness(6, 4),
                Children = { new HorizontalStackLayout { Spacing = 10, Children = { _summary, _keepAll } }, scroll }
            };
            UpdateHeader();
        }

        public View View { get; }

        // A capture pushed out or cleared: its thumbnail goes, and the row with the last one.
        public void Remove(CameraCapture capture)
        {
            int index = _items.IndexOf(capture);
            if (index < 0) return;
            _items.RemoveAt(index);
            _thumbnails.Children.RemoveAt(index);
            if (_items.Count == 0) _feature.Host.RemoveCard(View);
            else UpdateHeader();
        }

        // A capture was kept: its thumbnail gets the check mark.
        public void Refresh(CameraCapture capture)
        {
            int index = _items.IndexOf(capture);
            if (index < 0) return;
            _thumbnails.Children[index] = _feature.CaptureThumbnail(capture, CaptureRowThumbnailHeight, markKept: true);
            UpdateHeader();
        }

        private void UpdateHeader()
        {
            int kept = _items.Count(c => c.Kept);
            _summary.Text = "Camera · " + CameraCaptureFile.Summary(_items) + (_restored ? " · kept in this chat" : kept > 0 ? " · " + kept + " kept" : "");
            _keepAll.IsVisible = !_restored && _feature.CanKeepCaptures(_reply) && kept < _items.Count;
        }

        private async Task KeepAllAsync()
        {
            if (!_keepAll.IsEnabled) return;
            _keepAll.IsEnabled = false;
            try
            {
                foreach (CameraCapture capture in _items.Where(c => !c.Kept).Reverse().ToList())
                    await _feature.KeepCaptureAsync(capture, _reply);
            }
            catch (Exception ex) { await _feature.Host.AskAsync("Couldn't keep them in the chat", ex.Message, "OK"); }
            finally
            {
                _keepAll.IsEnabled = true;
                UpdateHeader();
            }
        }
    }
}
