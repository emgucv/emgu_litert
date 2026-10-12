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
    /// time and the calculator always, memories about the user (ChatStore) when memory is on, the user's location (this
    /// file's MAUI lookup) when they allow it, and with web search on the weather, Wikipedia and - with a Tavily API key -
    /// a web search and a web page reader.
    /// </summary>
    internal static class ChatAgentTools
    {
        public static IList<AITool> Create(string tavilyApiKey, bool webSearch, bool location, ChatStore memories)
        {
            List<AITool> tools = new List<AITool> { AgentTools.CurrentDateTime(), AgentTools.Calculate() };
            if (memories != null)
                tools.AddRange(AgentTools.Memory(memories));
            if (location)
                tools.Add(AgentTools.Location(GetCurrentLocationAsync));
            if (!webSearch)
                return tools;
            tools.Add(AgentTools.Weather());
            tools.Add(AgentTools.Wikipedia());
            if (!string.IsNullOrWhiteSpace(tavilyApiKey))
            {
                tools.Add(AgentTools.TavilySearch(tavilyApiKey));
                tools.Add(AgentTools.TavilyReadPage(tavilyApiKey));
            }
            return tools;
        }

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
