//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Chat;
using Microsoft.Extensions.AI;

namespace Emgu.LiteRT.LM.Maui.Features
{
    /// <summary>
    /// Places near the user (<see cref="AgentTools.SearchPlaces"/>: "what is the nearest Halloween store?"), with the Web
    /// search chip on, since the query and the position leave the device. Which search answers: with a Google Places API key
    /// (Settings > Places search, kept in secure storage as <see cref="KeyStorageName"/>) Google Maps
    /// (<see cref="GooglePlacesSearch"/>); without one Apple Maps on iOS and Mac Catalyst (<see cref="AppleMapsPlaceSearch"/>),
    /// and OpenStreetMap (<see cref="OpenStreetMapPlaceSearch"/>, no key) on Android and Windows - also when the first one
    /// fails. "Near the user" is the device's position, which needs "Share my location" (<see cref="LocationFeature"/>);
    /// a town the user names in the message works without it.
    /// </summary>
    public sealed class PlacesFeature : ChatFeature
    {
        /// <summary>The secure storage name of the Google Places API key: "google_places_api_key".</summary>
        public const string KeyStorageName = "google_places_api_key";

        private readonly WebFeature.ApiKeyRow _googleKey;
        private readonly PlaceSearchService _service;
        private Label _provider;
        private string _googleApiKey;

        /// <summary>Create the feature.</summary>
        public PlacesFeature()
        {
            _service = new PlaceSearchService(PlatformSearch(), null, DeviceLocationAsync) { NoPositionMessage = NoPositionMessage };
            _googleKey = new WebFeature.ApiKeyRow(KeyStorageName, "Google Places API key (optional)", key =>
            {
                _googleApiKey = key;
                ChooseProvider();
            });
        }

        /// <inheritdoc/>
        public override string Name => "places";

        /// <inheritdoc/>
        public override string Title => "Places search";

        /// <inheritdoc/>
        public override bool PersistEnabled => false;

        /// <summary>On when Web search is (the search sends the query and the position out); not switched on its own.</summary>
        public override bool Enabled
        {
            get => Host?.Services.Get<WebFeature>()?.Enabled == true;
            set { }
        }

        /// <inheritdoc/>
        public override string ToolSummary => "places near the user";

        /// <inheritdoc/>
        public override void OnAttached(IChatPageHost host)
        {
            base.OnAttached(host);
            _service.SettingsLocation = host.SettingsLocation + " > Share my location";
            _ = _googleKey.LoadAsync();
        }

        /// <inheritdoc/>
        public override IEnumerable<AITool> Tools(ChatMessageContext context) => new AITool[] { AgentTools.SearchPlaces(_service.SearchAsync) };

        /// <inheritdoc/>
        public override Task<string> InstructionsAsync(ChatMessageContext context) => Task.FromResult(
            "For shops, restaurants and other places near the user (\"what is the nearest Halloween store?\", \"where can I buy a charger?\"), use search_places with what they want: " +
            "it knows where the user is, so don't put a town in the query or in near unless the user named it in this message. Give the closest few with their addresses and distances.");

        /// <inheritdoc/>
        public override View BuildSettingsSection()
        {
            _provider = ChatPage.Note(string.Empty);
            ChooseProvider();
            return new VerticalStackLayout
            {
                Spacing = 6,
                Children =
                {
                    ChatPage.SectionTitle("Places search"),
                    ChatPage.Note("Finds shops, restaurants and other places near you (\"what is the nearest pharmacy?\") when Web search is on. With a Google Places API key (Google Cloud, Places API (New) enabled and billing on - Google charges per search) it uses Google Maps. Without one it uses Apple Maps on iPhone, iPad and Mac, and OpenStreetMap on Android and Windows (no ratings or opening hours, and fewer places). \"Near you\" needs Share my location; the search text and your approximate position go to the service."),
                    _googleKey.Build(),
                    _provider
                }
            };
        }

        // Google with a key; else the platform's own search.
        private void ChooseProvider()
        {
            bool google = !string.IsNullOrWhiteSpace(_googleApiKey);
            _service.Provider = google ? new GooglePlacesSearch(_googleApiKey) : PlatformSearch();
            // The key may be wrong or over its quota: the platform's search then answers.
            _service.Fallback = google ? PlatformSearch() : FallbackSearch();
            if (_provider != null)
                MainThread.BeginInvokeOnMainThread(() => _provider.Text = "Searching with " + _service.Provider.Name + ".");
        }

        // Apple Maps on Apple platforms, OpenStreetMap elsewhere.
        private static IPlaceSearch PlatformSearch()
        {
#if IOS || MACCATALYST
            return new AppleMapsPlaceSearch();
#else
            return new OpenStreetMapPlaceSearch();
#endif
        }

        // Where the platform's own search fails: OpenStreetMap (itself, nothing).
        private static IPlaceSearch FallbackSearch()
        {
#if IOS || MACCATALYST
            return new OpenStreetMapPlaceSearch();
#else
            return null;
#endif
        }

        // With "Share my location" off: how to turn it on (the model passes it on); null for other reasons.
        private string NoPositionMessage()
        {
            LocationFeature location = Host?.Services.Get<LocationFeature>();
            if (location == null || location.Enabled)
                return null;
            return "The user hasn't turned on \"Share my location\", so their position isn't known. Tell them to turn it on in " + Host.SettingsLocation
                + " (the section \"Share my location\"; the device asks for permission the first time) and then ask again - or ask which town to search.";
        }

        // The device's position, when the user shares it.
        private async Task<GeoPoint> DeviceLocationAsync(CancellationToken cancellationToken)
        {
            LocationFeature location = Host?.Services.Get<LocationFeature>();
            if (location == null || !location.Enabled)
                return null;
            return await LocationFeature.GetGeoPointAsync(cancellationToken);
        }
    }
}
