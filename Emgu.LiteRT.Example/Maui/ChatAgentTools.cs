//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

#if WINDOWS || IOS || ANDROID || MACCATALYST

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Agents;
using Microsoft.Extensions.AI;

namespace Maui.Demo.Lite
{
    /// <summary>
    /// The tools the LiteRT-LM chat page's agent gets with a message, from Emgu.LiteRT.LM.Agents' AgentTools: the date and
    /// time, the calculator and unit conversion always, memories about the user (ChatStore) when memory is on, the user's
    /// location (this file's MAUI lookup) when they allow it, and with web search on the weather, Wikipedia, currency
    /// conversion and a web page reader (Tavily's, with a Tavily API key, which also adds a web search).
    /// </summary>
    internal static partial class ChatAgentTools
    {
        public static IList<AITool> Create(string tavilyApiKey, bool webSearch, bool location, ChatStore memories,
            Func<string, CancellationToken, Task<string>> findPhotos = null,
            Func<string, CancellationToken, Task<string>> generateImage = null,
            Func<string, CancellationToken, Task<string>> searchChats = null, bool privateChat = false,
            Func<string, string, CancellationToken, Task<string>> searchDocuments = null,
            bool readClipboard = false)
        {
            var tools = new List<AITool>(SessionTools(privateChat, memories, findPhotos, generateImage, searchChats, searchDocuments));
            if (location)
                tools.Add(AgentTools.Location(GetCurrentLocationAsync));
            // Only for a message that asks about what the user copied: a clipboard can hold passwords.
            if (readClipboard)
                tools.Add(AgentTools.ReadClipboard(ReadClipboardAsync));
            if (!webSearch)
                return tools;
            tools.Add(AgentTools.Weather());
            tools.Add(AgentTools.Wikipedia());
            tools.Add(AgentTools.ConvertCurrency());
            if (!string.IsNullOrWhiteSpace(tavilyApiKey))
            {
                tools.Add(AgentTools.TavilySearch(tavilyApiKey));
                // Tavily's reader also reads pages built by scripts.
                tools.Add(AgentTools.TavilyReadPage(tavilyApiKey));
            }
            else
            {
                tools.Add(AgentTools.ReadWebPage());
            }
            return tools;
        }

        // The text on the clipboard, or null. Android 12+ and iOS tell the user that the app read it.
        public static Task<string> ReadClipboardAsync(CancellationToken cancellationToken) =>
            MainThread.InvokeOnMainThreadAsync(async () =>
                Clipboard.Default.HasText ? await Clipboard.Default.GetTextAsync() : null);

        // The user's location at about city level: the device's position (a recent known one, else a quick low-accuracy
        // fix), turned into a place name by the platform's geocoder (online: Apple / Google). Coordinates are rounded to
        // ~1 km. Permission is asked for the first time it is used; geolocation and permissions want the UI thread.
        public static async Task<string> GetCurrentLocationAsync(CancellationToken cancellationToken)
        {
            try
            {
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
                    Placemark placemark = null;
                    foreach (Placemark p in await Geocoding.Default.GetPlacemarksAsync(location.Latitude, location.Longitude))
                    {
                        placemark = p;
                        break;
                    }
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

#endif
