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
        public override void RegisterServices(ChatFeatureServices services) => services.Add(this);

        /// <inheritdoc/>
        public override IEnumerable<AITool> Tools(ChatMessageContext context) => new AITool[] { AgentTools.Location(GetCurrentLocationAsync) };

        /// <inheritdoc/>
        public override string ToolSummary => "the user's location";

        /// <inheritdoc/>
        public override string DisabledInstructions =>
            "Sharing the user's location is turned off, so you can't tell where they are. If a request needs it (places near them, local weather, their time zone), tell the user they can turn on \"Share my location\" in "
            + (Host?.SettingsLocation ?? "the chat's settings") + " (the device asks for permission the first time), or ask which town they mean.";

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
            DevicePosition position = await GetPositionAsync(cancellationToken);
            if (position.Error != null)
                return position.Error;
            string coordinates = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "latitude {0:F2}, longitude {1:F2}", position.Latitude, position.Longitude);
            return string.IsNullOrEmpty(position.Place)
                ? "The user is at " + coordinates + "."
                : "The user is in " + position.Place + " (" + coordinates + ").";
        }

        /// <summary>
        /// The device's position for the place search (<see cref="PlacesFeature"/>): the same lookup as the location tool - a recent
        /// known position, else a quick low-accuracy fix, rounded to ~1 km - or null when it isn't known or isn't allowed.
        /// </summary>
        /// <param name="cancellationToken">Cancels it</param>
        public static async Task<GeoPoint> GetGeoPointAsync(CancellationToken cancellationToken)
        {
            DevicePosition position = await GetPositionAsync(cancellationToken);
            return position.Error != null ? null : new GeoPoint { Latitude = position.Latitude, Longitude = position.Longitude, Name = position.Place };
        }

        // Where the device is: coordinates rounded to 2 decimals and the place's name when a geocoder gives one - or why not.
        private sealed class DevicePosition
        {
            public double Latitude;
            public double Longitude;
            public string Place;
            public string Error;
        }

        private static async Task<DevicePosition> GetPositionAsync(CancellationToken cancellationToken)
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
                    return new DevicePosition { Error = "The user hasn't allowed this app to use their location." };

                Location location = await MainThread.InvokeOnMainThreadAsync(() => Geolocation.Default.GetLastKnownLocationAsync());
                if (location == null || DateTimeOffset.UtcNow - location.Timestamp > TimeSpan.FromMinutes(30))
                {
                    GeolocationRequest request = new GeolocationRequest(GeolocationAccuracy.Low, TimeSpan.FromSeconds(15));
                    location = await MainThread.InvokeOnMainThreadAsync(() => Geolocation.Default.GetLocationAsync(request, cancellationToken))
                        ?? location;
                }
                if (location == null)
                    return new DevicePosition { Error = "The device couldn't determine its location." };

                DevicePosition position = new DevicePosition
                {
                    Latitude = Math.Round(location.Latitude, 2),
                    Longitude = Math.Round(location.Longitude, 2)
                };
                try
                {
                    Placemark placemark = (await Geocoding.Default.GetPlacemarksAsync(location.Latitude, location.Longitude)).FirstOrDefault();
                    if (placemark != null)
                    {
                        List<string> parts = new List<string>();
                        foreach (string part in new[] { placemark.SubLocality, placemark.Locality, placemark.AdminArea, placemark.CountryName })
                            if (!string.IsNullOrWhiteSpace(part) && !parts.Contains(part))
                                parts.Add(part);
                        position.Place = string.Join(", ", parts);
                    }
                }
                catch (Exception)
                {
                    // No geocoder (e.g. Windows without a map service key) or no network: the coordinates only.
                }
                return position;
            }
            catch (FeatureNotEnabledException)
            {
                return new DevicePosition { Error = "Location services are turned off on this device." };
            }
            catch (FeatureNotSupportedException)
            {
                return new DevicePosition { Error = "This device can't determine its location." };
            }
            catch (PermissionException)
            {
                return new DevicePosition { Error = "The user hasn't allowed this app to use their location." };
            }
        }
    }
}
