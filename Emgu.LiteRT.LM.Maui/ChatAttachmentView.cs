//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Microsoft.Extensions.AI;

namespace Emgu.LiteRT.LM.Maui
{
    /// <summary>
    /// An attachment of a message, or one waiting to be sent: an image as a thumbnail, audio as a labelled chip, with a
    /// remove button when <see cref="CanRemove"/>.
    /// <para>
    /// Its look is the "ChatAttachmentViewTemplate" template (see <see cref="ChatStyles"/>), with the parts "PART_Image" (an
    /// Image, shown for images), "PART_Label" (a Label, shown for audio) and "PART_Remove" (tapped to remove). A template
    /// without one of them just doesn't show that part.
    /// </para>
    /// </summary>
    public class ChatAttachmentView : TemplatedView
    {
        private const string TemplateKey = "ChatAttachmentViewTemplate";

        /// <summary>The attachment.</summary>
        public static readonly BindableProperty AttachmentProperty = BindableProperty.Create(
            nameof(Attachment), typeof(DataContent), typeof(ChatAttachmentView), null, propertyChanged: (b, o, n) => ((ChatAttachmentView)b).Update());

        /// <summary>True to show the remove button (for an attachment not sent yet).</summary>
        public static readonly BindableProperty CanRemoveProperty = BindableProperty.Create(
            nameof(CanRemove), typeof(bool), typeof(ChatAttachmentView), false, propertyChanged: (b, o, n) => ((ChatAttachmentView)b).Update());

        private Image _image;
        private Label _label;
        private View _remove;

        /// <summary>The attachment.</summary>
        public DataContent Attachment
        {
            get => (DataContent)GetValue(AttachmentProperty);
            set => SetValue(AttachmentProperty, value);
        }

        /// <summary>True to show the remove button (for an attachment not sent yet).</summary>
        public bool CanRemove
        {
            get => (bool)GetValue(CanRemoveProperty);
            set => SetValue(CanRemoveProperty, value);
        }

        /// <summary>Raised when the remove button is tapped.</summary>
        public event EventHandler RemoveRequested;

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
            _image = GetTemplateChild("PART_Image") as Image;
            _label = GetTemplateChild("PART_Label") as Label;
            _remove = GetTemplateChild("PART_Remove") as View;
            if (_remove != null)
            {
                TapGestureRecognizer tap = new TapGestureRecognizer();
                tap.Tapped += (s, e) => RemoveRequested?.Invoke(this, EventArgs.Empty);
                _remove.GestureRecognizers.Add(tap);
                SemanticProperties.SetDescription(_remove, "Remove the attachment");
            }
            Update();
        }

        private void Update()
        {
            DataContent attachment = Attachment;
            bool isImage = attachment != null && attachment.HasTopLevelMediaType("image");
            if (_image != null)
            {
                _image.IsVisible = isImage;
                if (isImage)
                {
                    byte[] data = attachment.Data.ToArray();
                    _image.Source = ImageSource.FromStream(() => new MemoryStream(data));
                }
            }
            if (_label != null)
            {
                _label.IsVisible = attachment != null && !isImage;
                _label.Text = attachment == null || isImage ? string.Empty : Describe(attachment);
            }
            if (_remove != null)
                _remove.IsVisible = CanRemove;
        }

        // "🎤 recording.wav · 5 s"
        private static string Describe(DataContent attachment)
        {
            string name = string.IsNullOrEmpty(attachment.Name) ? "Audio" : attachment.Name;
            double seconds = WavSeconds(attachment.Data.Span);
            return "\U0001F3A4 " + name + (seconds > 0 ? string.Format(" · {0:0} s", Math.Max(1, seconds)) : string.Empty);
        }

        private static double WavSeconds(ReadOnlySpan<byte> data)
        {
            if (data.Length > 44 && data[0] == (byte)'R' && data[1] == (byte)'I' && data[8] == (byte)'W' && data[9] == (byte)'A')
            {
                int byteRate = BitConverter.ToInt32(data.Slice(28, 4));
                if (byteRate > 0)
                    return (data.Length - 44) / (double)byteRate;
            }
            return 0;
        }
    }
}
