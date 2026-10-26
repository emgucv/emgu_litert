//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.LiteRT.LM.Maui;

namespace Maui.Demo.Lite;

/// <summary>The settings' CPU thread slider for Emgu.LiteRT.LM.Maui's <see cref="CpuThreadSettings"/>.</summary>
internal static class CpuThreadsView
{
    public static View Create(Func<Task> reloadModels)
    {
        int maximum = CpuThreadSettings.Maximum;
        var label = new Label
        {
            FontFamily = Theme.BodyFont, FontSize = 15, TextColor = Theme.PrimaryText,
            Text = $"CPU threads: {CpuThreadSettings.Count} of {maximum}"
        };
        var slider = new Slider
        {
            Minimum = maximum > 1 ? 1 : 0, Maximum = maximum, Value = CpuThreadSettings.Count,
            IsEnabled = maximum > 1, MinimumTrackColor = Theme.Accent
        };
        SemanticProperties.SetDescription(slider, "CPU threads used by all models");
        slider.ValueChanged += (sender, args) =>
        {
            CpuThreadSettings.Count = (int)Math.Round(args.NewValue);
            label.Text = $"CPU threads: {CpuThreadSettings.Count} of {maximum}";
        };
        slider.DragCompleted += (sender, args) => slider.Value = CpuThreadSettings.Count;
        var note = new Label
        {
            Text = "Choose a thread count, then apply it to reload models without restarting the app.",
            FontFamily = Theme.BodyFont, FontSize = 12, TextColor = Theme.SecondaryText
        };
        var apply = new Button { Text = "Apply and reload models" };
        apply.Clicked += async (sender, args) =>
        {
            apply.IsEnabled = slider.IsEnabled = false;
            note.Text = "Waiting for active work, then reloading models...";
            try
            {
                await reloadModels();
                note.Text = $"Models now use {CpuThreadSettings.Count} CPU threads. TF Lite demos reload on their next run.";
            }
            catch (Exception ex)
            {
                note.Text = "Could not reload models: " + ex.Message + " You can try again.";
            }
            finally
            {
                apply.IsEnabled = true;
                slider.IsEnabled = maximum > 1;
            }
        };
        return new VerticalStackLayout
        {
            Spacing = 6,
            Children =
            {
                label, slider, apply, note
            }
        };
    }
}
