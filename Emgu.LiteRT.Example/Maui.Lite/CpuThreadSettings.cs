using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Maui.Demo.Lite;

/// <summary>Shared CPU compute budget for models loaded by the demo.</summary>
internal static class CpuThreadSettings
{
    private const string PreferenceKey = "model_cpu_threads";
    public static int Maximum { get; } = GetCoreCount();
    public static int ReloadVersion { get; private set; }
    public static void RequestReload() => ReloadVersion++;

    public static int Count
    {
        get => Math.Clamp(Preferences.Default.Get(PreferenceKey, Math.Min(4, Maximum)), 1, Maximum);
        set => Preferences.Default.Set(PreferenceKey, Math.Clamp(value, 1, Maximum));
    }

    public static View CreateView(Func<Task> reloadModels)
    {
        var label = new Label
        {
            FontFamily = Theme.BodyFont, FontSize = 15, TextColor = Theme.PrimaryText,
            Text = $"CPU threads: {Count} of {Maximum}"
        };
        var slider = new Slider
        {
            Minimum = Maximum > 1 ? 1 : 0, Maximum = Maximum, Value = Count,
            IsEnabled = Maximum > 1, MinimumTrackColor = Theme.Accent
        };
        SemanticProperties.SetDescription(slider, "CPU threads used by all models");
        slider.ValueChanged += (sender, args) =>
        {
            Count = (int)Math.Round(args.NewValue);
            label.Text = $"CPU threads: {Count} of {Maximum}";
        };
        slider.DragCompleted += (sender, args) => slider.Value = Count;
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
                note.Text = $"Models now use {Count} CPU threads. TF Lite demos reload on their next run.";
            }
            catch (Exception ex)
            {
                note.Text = "Could not reload models: " + ex.Message + " You can try again.";
            }
            finally
            {
                apply.IsEnabled = true;
                slider.IsEnabled = Maximum > 1;
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

    private static int GetCoreCount()
    {
#if WINDOWS
        // Count physical cores across all sockets, rather than SMT logical processors.
        uint length = 0;
        GetLogicalProcessorInformationEx(0, IntPtr.Zero, ref length);
        if (length > 0)
        {
            IntPtr buffer = Marshal.AllocHGlobal(checked((int)length));
            try
            {
                if (GetLogicalProcessorInformationEx(0, buffer, ref length))
                {
                    int count = 0;
                    for (int offset = 0; offset + 8 <= length;)
                    {
                        IntPtr entry = IntPtr.Add(buffer, offset);
                        int size = Marshal.ReadInt32(entry, 4);
                        if (size < 8 || size > length - offset)
                            break;
                        if (Marshal.ReadInt32(entry) == 0)
                            count++;
                        offset += size;
                    }
                    if (count > 0)
                        return count;
                }
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
#endif
        return Math.Max(1, Environment.ProcessorCount);
    }

#if WINDOWS
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLogicalProcessorInformationEx(int relationship, IntPtr buffer, ref uint length);
#endif
}
