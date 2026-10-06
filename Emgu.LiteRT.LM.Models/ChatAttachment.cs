//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.IO;
using System.Text.Json;

namespace Emgu.LiteRT.LM.Models
{
    /// <summary>
    /// The kind of a chat attachment
    /// </summary>
    public enum ChatAttachmentType
    {
        /// <summary>
        /// An image
        /// </summary>
        Image,
        /// <summary>
        /// An audio clip
        /// </summary>
        Audio,
    }

    /// <summary>
    /// An image or audio clip sent with a chat message, for models that accept them
    /// (LanguageModel.SupportsImages / SupportsAudio). LiteRT-LM decodes it: images with stb (e.g. JPEG, PNG, BMP),
    /// audio with miniaudio (e.g. WAV, MP3, FLAC), resampled to the model's sample rate.
    /// </summary>
    public class ChatAttachment
    {
        private ChatAttachment(ChatAttachmentType type, String path, byte[] data)
        {
            Type = type;
            Path = path;
            Data = data;
        }

        /// <summary>
        /// The kind of attachment
        /// </summary>
        public ChatAttachmentType Type { get; }

        /// <summary>
        /// The path of the file LiteRT-LM reads, or null if the content is given as Data
        /// </summary>
        public String Path { get; }

        /// <summary>
        /// The encoded file content (e.g. JPEG or WAV bytes), or null if the content is read from Path
        /// </summary>
        public byte[] Data { get; }

        /// <summary>
        /// An image read from a file when the message is sent.
        /// </summary>
        /// <param name="path">The image file</param>
        /// <returns>The attachment</returns>
        public static ChatAttachment ImageFile(String path)
        {
            return new ChatAttachment(ChatAttachmentType.Image, GetFullPath(path), null);
        }

        /// <summary>
        /// An image from an encoded image file's content.
        /// </summary>
        /// <param name="encodedImage">The encoded image, e.g. JPEG or PNG bytes</param>
        /// <returns>The attachment</returns>
        public static ChatAttachment Image(byte[] encodedImage)
        {
            return new ChatAttachment(ChatAttachmentType.Image, null, CheckData(encodedImage, "encodedImage"));
        }

        /// <summary>
        /// An audio clip read from a file when the message is sent.
        /// </summary>
        /// <param name="path">The audio file</param>
        /// <returns>The attachment</returns>
        public static ChatAttachment AudioFile(String path)
        {
            return new ChatAttachment(ChatAttachmentType.Audio, GetFullPath(path), null);
        }

        /// <summary>
        /// An audio clip from an encoded audio file's content.
        /// </summary>
        /// <param name="encodedAudio">The encoded audio, e.g. WAV bytes</param>
        /// <returns>The attachment</returns>
        public static ChatAttachment Audio(byte[] encodedAudio)
        {
            return new ChatAttachment(ChatAttachmentType.Audio, null, CheckData(encodedAudio, "encodedAudio"));
        }

        /// <summary>
        /// Describe the attachment
        /// </summary>
        /// <returns>The attachment type and its file name or size</returns>
        public override String ToString()
        {
            return String.Format("{0} {1}", Type, Path != null ? System.IO.Path.GetFileName(Path) : String.Format("({0} bytes)", Data.Length));
        }

        /// <summary>
        /// Write the attachment as a content part in LiteRT-LM's JSON message format:
        /// {"type": "image" or "audio", "path": ...} or {"type": ..., "blob": base64}
        /// </summary>
        internal void WriteJson(Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            writer.WriteString("type", Type == ChatAttachmentType.Image ? "image" : "audio");
            if (Path != null)
                writer.WriteString("path", Path);
            else
                writer.WriteBase64String("blob", Data);
            writer.WriteEndObject();
        }

        // LiteRT-LM opens the file itself, so resolve it against this process's current folder now.
        private static String GetFullPath(String path)
        {
            if (String.IsNullOrEmpty(path))
                throw new ArgumentNullException("path");
            return System.IO.Path.GetFullPath(path);
        }

        private static byte[] CheckData(byte[] data, String name)
        {
            if (data == null || data.Length == 0)
                throw new ArgumentException("The content is empty", name);
            return data;
        }
    }
}
