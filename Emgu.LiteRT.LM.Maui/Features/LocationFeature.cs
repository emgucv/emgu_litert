//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.LiteRT.LM.Agents;
using Microsoft.Extensions.AI;

namespace Emgu.LiteRT.LM.Maui.Features
{
    /// <summary>
    /// The user's location, at about city level, for the agent (<see cref="AgentTools.Location"/>): the device's position
    /// (a recent known one, else a quick low-accuracy fix) turned into a place name by the platform's geocoder (online:
    /// Apple / Google), coordinates rounded to ~1 km. Off until the user turns "Share my location" on in the settings
    /// (kept as "share_location"); the device asks for permission the first time it is used. The app declares the
    /// permission: ACCESS_COARSE_LOCATION on Android, NSLocationWhenInUseUsageDescription on iOS and Mac Catalyst.
    /// </summary>
    public sealed class LocationFeature : ChatFeature
    {
        private const string Key = "share_location";

        /// <inheritdoc/>
        public override string Name => "location";

        /// <inheritdoc/>
        public override string Title => "Share my location";

        /// <inheritdoc/>
        public override bool PersistEnabled => false;

        /// <inheritdoc/>
        public override void OnAttached(IChatPageHost host)
        {
            base.OnAttached(host);
            Enabled = host.Settings.Get(Key, false);
        }

        /// <inheritdoc/>
        public override IEnumerable<AITool> Tools(ChatMessageContext context) => new AITool[] { AgentTools.Location(GetCurrentLocationAsync) };

        /// <inheritdoc/>
        public override string ToolSummary => "the user's location";

        /// <inheritdoc/>
        public override View BuildSettingsSection() =>
            ChatPage.SwitchRow("Share my location",
                "Lets the assistant look up your town and approximate position, for questions such as \"what's the weather here?\". Your device asks for permission the first time. The place name comes from Apple's or Google's map service, and web searches that use it leave the device.",
                Enabled, on =>
                {
                    Enabled = on;
                    Host?.Settings.Set(Key, on);
                });

        /// <summary>The user's location at about city level, in words for the model (or why it isn't known).</summary>
        /// <param name="cancellationToken">Cancels it</param>
        public static async Task<string> GetCurrentLocationAsync(CancellationToken cancellationToken)
        {
            try
            {
                // Geolocation and permissions want the UI thread.
                PermissionStatus status = await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    PermissionStatus current = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
                    return current == PermissionStatus.Granted ? current : await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
                });
                if (status != PermissionStatus.Granted)
                    return "The user hasn't allowed this app to use their location.";

                Location location = await MainThread.InvokeOnMainThreadAsync(() => Geolocation.Default.GetLastKnownLocationAsync());
                if (location == null || DateTimeOffset.UtcNow - location.Timestamp > TimeSpan.FromMinutes(30))
                {
                    GeolocationRequest request = new GeolocationRequest(GeolocationAccuracy.Low, TimeSpan.FromSeconds(15));
                    location = await MainThread.InvokeOnMainThreadAsync(() => Geolocation.Default.GetLocationAsync(request, cancellationToken))
                        ?? location;
                }
                if (location == null)
                    return "The device couldn't determine its location.";

                string coordinates = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "latitude {0:F2}, longitude {1:F2}", location.Latitude, location.Longitude);
                string place = null;
                try
                {
                    Placemark placemark = (await Geocoding.Default.GetPlacemarksAsync(location.Latitude, location.Longitude)).FirstOrDefault();
                    if (placemark != null)
                    {
                        List<string> parts = new List<string>();
                        foreach (string part in new[] { placemark.SubLocality, placemark.Locality, placemark.AdminArea, placemark.CountryName })
                            if (!string.IsNullOrWhiteSpace(part) && !parts.Contains(part))
                                parts.Add(part);
                        place = string.Join(", ", parts);
                    }
                }
                catch (Exception)
                {
                    // No geocoder (e.g. Windows without a map service key) or no network: the coordinates only.
                }
                return string.IsNullOrEmpty(place)
                    ? "The user is at " + coordinates + "."
                    : "The user is in " + place + " (" + coordinates + ").";
            }
            catch (FeatureNotEnabledException)
            {
                return "Location services are turned off on this device.";
            }
            catch (FeatureNotSupportedException)
            {
                return "This device can't determine its location.";
            }
            catch (PermissionException)
            {
                return "The user hasn't allowed this app to use their location.";
            }
        }
    }
}
