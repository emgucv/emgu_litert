//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

namespace Emgu.LiteRT.LM.Maui
{
    /// <summary>
    /// A settings section for <see cref="CpuThreadSettings"/>: a slider for the CPU threads all of an app's models run
    /// with, and "Apply and reload models". Styled by the "ChatSection*" keys (<see cref="ChatStyles"/>).
    /// </summary>
    public static class CpuThreadsView
    {
        /// <summary>Build the section.</summary>
        /// <param name="reloadModels">Reloads the models with the new count (e.g. a chat page's <c>ReloadModelAsync</c>)</param>
        public static View Create(Func<Task> reloadModels)
        {
            int maximum = CpuThreadSettings.Maximum;
            var label = new Label { Text = $"CPU threads: {CpuThreadSettings.Count} of {maximum}" };
            ChatResources.ApplyStyle(label, "ChatSectionTextStyle");
            var slider = new Slider
            {
                Minimum = maximum > 1 ? 1 : 0, Maximum = maximum, Value = CpuThreadSettings.Count,
                IsEnabled = maximum > 1, MinimumTrackColor = ChatResources.FindColor("ChatAccent")
            };
            SemanticProperties.SetDescription(slider, "CPU threads used by all models");
            slider.ValueChanged += (sender, args) =>
            {
                CpuThreadSettings.Count = (int)Math.Round(args.NewValue);
                label.Text = $"CPU threads: {CpuThreadSettings.Count} of {maximum}";
            };
            slider.DragCompleted += (sender, args) => slider.Value = CpuThreadSettings.Count;
            var note = new Label { Text = "Choose a thread count, then apply it to reload the models without restarting the app." };
            ChatResources.ApplyStyle(note, "ChatSectionNoteStyle");
            var apply = new Button { Text = "Apply and reload models" };
            ChatResources.ApplyStyle(apply, "ChatStopButtonStyle");
            apply.Clicked += async (sender, args) =>
            {
                apply.IsEnabled = slider.IsEnabled = false;
                note.Text = "Waiting for active work, then reloading the models...";
                try
                {
                    await reloadModels();
                    note.Text = $"The models now use {CpuThreadSettings.Count} CPU threads.";
                }
                catch (Exception ex)
                {
                    note.Text = "Could not reload the models: " + ex.Message + " You can try again.";
                }
                finally
                {
                    apply.IsEnabled = true;
                    slider.IsEnabled = maximum > 1;
                }
            };
            return new VerticalStackLayout { Spacing = 6, Children = { label, slider, apply, note } };
        }
    }
}
