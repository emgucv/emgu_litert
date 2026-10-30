// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Agents;
using Microsoft.Extensions.AI;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// search_places: the Google Places and OpenStreetMap searches against canned HTTP replies, the service that picks the
// search point and writes the text for the model, and the tool. No network, no model.
[TestFixture]
public class UnitTestPlaceSearch
{
    // Richmond Hill, Ontario.
    private static readonly GeoPoint Home = new GeoPoint { Latitude = 43.88, Longitude = -79.44, Name = "Richmond Hill, Ontario, Canada" };

    private sealed class Handler : HttpMessageHandler
    {
        public readonly List<(HttpRequestMessage Request, string Body)> Requests = new();
        public Func<HttpRequestMessage, string, (HttpStatusCode Status, string Json)> Reply;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            string body = request.Content == null ? null : await request.Content.ReadAsStringAsync();
            Requests.Add((request, body));
            (HttpStatusCode status, string json) = Reply(request, body);
            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    private static string GoogleReply(params (string Name, double Lat, double Lon, bool? Open, double? Rating)[] places) =>
        JsonSerializer.Serialize(new
        {
            places = places.Select(p => new Dictionary<string, object>
            {
                ["displayName"] = new { text = p.Name, languageCode = "en" },
                ["formattedAddress"] = "1 Main St, Richmond Hill, ON",
                ["location"] = new { latitude = p.Lat, longitude = p.Lon },
                ["primaryTypeDisplayName"] = new { text = "Costume store" },
                ["rating"] = p.Rating ?? 0,
                ["userRatingCount"] = 120,
                ["currentOpeningHours"] = new { openNow = p.Open ?? false },
                ["nationalPhoneNumber"] = "(905) 555-0100",
                ["websiteUri"] = "https://example.com",
                ["googleMapsUri"] = "https://maps.google.com/?cid=" + p.Name.Replace(' ', '-')
            }).ToArray()
        });

    [Test]
    public async Task GoogleSearchSendsTheKeyTheQueryAndACircleAndListsTheClosestFirst()
    {
        Handler handler = new Handler
        {
            // The second place is nearer than the first.
            Reply = (_, _) => (HttpStatusCode.OK, GoogleReply(("Far Costumes", 44.08, -79.44, true, 4.1), ("Near Costumes", 43.89, -79.44, false, 3.9)))
        };
        GooglePlacesSearch search = new GooglePlacesSearch("test-key-123", new HttpClient(handler));
        PlaceSearchService service = new PlaceSearchService(search, deviceLocation: _ => Task.FromResult(Home));

        string text = await service.SearchAsync("Halloween store", "", CancellationToken.None);

        (HttpRequestMessage request, string body) = handler.Requests.Single();
        Assert.That(request.Method, Is.EqualTo(HttpMethod.Post));
        Assert.That(request.RequestUri.AbsoluteUri, Is.EqualTo("https://places.googleapis.com/v1/places:searchText"));
        Assert.That(request.Headers.GetValues("X-Goog-Api-Key").Single(), Is.EqualTo("test-key-123"));
        Assert.That(request.Headers.GetValues("X-Goog-FieldMask").Single(), Does.Contain("places.displayName").And.Contain("places.location"));
        using (JsonDocument doc = JsonDocument.Parse(body))
        {
            Assert.That(doc.RootElement.GetProperty("textQuery").GetString(), Is.EqualTo("Halloween store"));
            JsonElement circle = doc.RootElement.GetProperty("locationBias").GetProperty("circle");
            Assert.That(circle.GetProperty("center").GetProperty("latitude").GetDouble(), Is.EqualTo(43.88));
            Assert.That(circle.GetProperty("radius").GetDouble(), Is.EqualTo(30000));
        }
        Assert.That(text.IndexOf("Near Costumes", StringComparison.Ordinal), Is.LessThan(text.IndexOf("Far Costumes", StringComparison.Ordinal)),
            "The nearer place comes first");
        Assert.That(text, Does.Contain("near Richmond Hill, Ontario, Canada").And.Contain("closed now").And.Contain("open now")
            .And.Contain("rated 4.1").And.Contain("(Google Maps)").And.Contain("not instructions"));
        Assert.That(text, Does.Contain("1.1 km"), "0.01 degrees of latitude is 1.1 km");
        Assert.That(text, Does.Contain("map: https://maps.google.com/?cid=Near-Costumes"), "Google's own link is used");
        Assert.That(text, Does.Contain("[Name](map link)"), "The model is told to link the names");
        Assert.That(text, Does.Not.Contain("test-key-123"));
    }

    [Test]
    public async Task AGoogleErrorIsReportedWithoutTheKey()
    {
        Handler handler = new Handler
        {
            Reply = (_, _) => (HttpStatusCode.Forbidden, "{\"error\":{\"code\":403,\"message\":\"API key not valid. Please pass a valid API key.\",\"status\":\"PERMISSION_DENIED\"}}")
        };
        PlaceSearchService service = new PlaceSearchService(new GooglePlacesSearch("secret-key-xyz", new HttpClient(handler)),
            deviceLocation: _ => Task.FromResult(Home));

        string text = await service.SearchAsync("pharmacy", "", CancellationToken.None);

        Assert.That(text, Does.StartWith("The place search failed:").And.Contain("HTTP 403").And.Contain("API key not valid"));
        Assert.That(text, Does.Not.Contain("secret-key-xyz"));
    }

    [Test]
    public async Task OpenStreetMapTriesTheSimplerWordsThenAWiderBoxAndKeepsTheNameOutOfTheAddress()
    {
        Handler handler = new Handler();
        handler.Reply = (request, _) =>
        {
            string url = request.RequestUri.AbsoluteUri;
            // Nothing for "Halloween store"; "Halloween" finds a shop.
            if (url.Contains("q=Halloween%20store"))
                return (HttpStatusCode.OK, "[]");
            return (HttpStatusCode.OK, JsonSerializer.Serialize(new[]
            {
                new Dictionary<string, object>
                {
                    ["name"] = "Spirit Halloween",
                    ["display_name"] = "Spirit Halloween, 100, Yonge Street, Oak Ridges, Richmond Hill, York Region, Ontario, L4C 1A1, Canada",
                    ["address"] = new Dictionary<string, string> { ["house_number"] = "100", ["road"] = "Yonge Street", ["city"] = "Richmond Hill", ["postcode"] = "L4C 1A1" },
                    ["lat"] = "43.90",
                    ["lon"] = "-79.44",
                    ["type"] = "party_supplies"
                },
                // Without address details: the first parts of the display name.
                new Dictionary<string, object>
                {
                    ["name"] = "Costume Corner",
                    ["display_name"] = "Costume Corner, 5 Elm Street, Richmond Hill, Ontario, Canada",
                    ["lat"] = "43.95",
                    ["lon"] = "-79.44",
                    ["type"] = "yes"
                }
            }));
        };
        OpenStreetMapPlaceSearch search = new OpenStreetMapPlaceSearch(new HttpClient(handler)) { Pause = TimeSpan.Zero };
        PlaceSearchService service = new PlaceSearchService(search, deviceLocation: _ => Task.FromResult(Home));

        string text = await service.SearchAsync("Halloween store", "", CancellationToken.None);

        Assert.That(handler.Requests.Count, Is.EqualTo(2));
        string url = handler.Requests[1].Request.RequestUri.AbsoluteUri;
        Assert.That(url, Does.StartWith("https://nominatim.openstreetmap.org/search?").And.Contain("bounded=1").And.Contain("q=Halloween"));
        Assert.That(url, Does.Contain("viewbox=-79.81"), "left edge: 0.27 degrees of latitude is 0.37 degrees of longitude here");
        Assert.That(text, Does.Contain("1. Spirit Halloween - party supplies - 100 Yonge Street, Richmond Hill - 2.2 km").And.Contain("(OpenStreetMap)"));
        Assert.That(text, Does.Contain("2. Costume Corner - 5 Elm Street, Richmond Hill, Ontario - 7.8 km"),
            "Without address details: the display name's first parts, without the name, and no category for \"yes\"");
        Assert.That(text, Does.Not.Contain("Spirit Halloween, 100"), "The address doesn't repeat the name");
        Assert.That(text, Does.Contain("map: https://www.google.com/maps/search/?api=1&query=Spirit%20Halloween%2C%20100%20Yonge%20Street%2C%20Richmond%20Hill"),
            "Without Google's own link, a Google Maps search for the name and address");
    }

    [Test]
    public async Task ANamedTownIsLookedUpAndSearchedInsteadOfTheDevicesPosition()
    {
        Handler geocoder = new Handler
        {
            Reply = (_, _) => (HttpStatusCode.OK,
                "{\"results\":[{\"name\":\"Ottawa\",\"admin1\":\"Ontario\",\"country\":\"Canada\",\"latitude\":45.41,\"longitude\":-75.7}]}")
        };
        double searched = 0;
        PlaceSearchService service = new PlaceSearchService(new Fake("Fake", (query, lat, lon) =>
        {
            searched = lat;
            return new List<PlaceResult> { new PlaceResult { Name = "Costume Shop", Latitude = 45.42, Longitude = -75.7 } };
        }), deviceLocation: _ => throw new InvalidOperationException("The device position must not be asked for"), http: new HttpClient(geocoder));

        string text = await service.SearchAsync("costumes", "Ottawa", CancellationToken.None);

        Assert.That(searched, Is.EqualTo(45.41));
        Assert.That(text, Does.Contain("near Ottawa, Ontario, Canada").And.Contain("Costume Shop"));
        Assert.That(geocoder.Requests.Single().Request.RequestUri.AbsoluteUri, Does.Contain("name=Ottawa"));
    }

    [Test]
    public async Task WithoutAPositionTheModelIsToldToAskForATown()
    {
        PlaceSearchService service = new PlaceSearchService(new Fake("Fake", (_, _, _) => new List<PlaceResult>()), deviceLocation: _ => Task.FromResult<GeoPoint>(null))
        {
            SettingsLocation = "Settings > Share my location"
        };

        string text = await service.SearchAsync("pharmacy", "", CancellationToken.None);

        Assert.That(text, Does.Contain("Ask which town").And.Contain("Settings > Share my location"));
        Assert.That(await service.SearchAsync("", "", CancellationToken.None), Does.StartWith("Say what to look for"));

        // The app can say why: sharing is off, and where to turn it on.
        service.NoPositionMessage = () => "The user hasn't turned on \"Share my location\" - tell them to turn it on in the settings.";
        Assert.That(await service.SearchAsync("pharmacy", "", CancellationToken.None), Does.StartWith("The user hasn't turned on"));
        // A null message falls back to the general one.
        service.NoPositionMessage = () => null;
        Assert.That(await service.SearchAsync("pharmacy", "", CancellationToken.None), Does.Contain("Ask which town"));
    }

    [Test]
    public async Task TheFallbackSearchIsUsedWhenTheFirstFails()
    {
        PlaceSearchService service = new PlaceSearchService(
            new Fake("Broken", (_, _, _) => throw new InvalidOperationException("no service")),
            new Fake("Backup", (_, lat, lon) => new List<PlaceResult> { new PlaceResult { Name = "Shop", Latitude = lat, Longitude = lon } }),
            _ => Task.FromResult(Home));

        string text = await service.SearchAsync("shop", "", CancellationToken.None);

        Assert.That(text, Does.Contain("1. Shop").And.Contain("(Backup)"));

        service.Fallback = null;
        Assert.That(await service.SearchAsync("shop", "", CancellationToken.None), Is.EqualTo("The place search failed: no service"));
    }

    [Test]
    public async Task NothingFoundIsSaidAndTheToolPassesItsArgumentsOn()
    {
        PlaceSearchService service = new PlaceSearchService(new Fake("Fake", (_, _, _) => new List<PlaceResult>()), deviceLocation: _ => Task.FromResult(Home));
        Assert.That(await service.SearchAsync("unicorn farm", "", CancellationToken.None),
            Is.EqualTo("No places found for \"unicorn farm\" near Richmond Hill, Ontario, Canada (Fake)."));

        string seenQuery = null, seenNear = null;
        AIFunction tool = AgentTools.SearchPlaces((query, near, _) =>
        {
            seenQuery = query;
            seenNear = near;
            return Task.FromResult("ok");
        });
        Assert.That(tool.Name, Is.EqualTo("search_places"));
        object result = await tool.InvokeAsync(new AIFunctionArguments { ["query"] = "Halloween store" });
        Assert.That(result?.ToString(), Does.Contain("ok"));
        Assert.That(seenQuery, Is.EqualTo("Halloween store"));
        Assert.That(seenNear, Is.EqualTo(string.Empty));

        Assert.That(AgentTools.Describe(new FunctionCallContent("1", "search_places", new Dictionary<string, object> { ["query"] = "pharmacy" })),
            Is.EqualTo("Looking for “pharmacy” near you"));
        Assert.That(AgentTools.Describe(new FunctionCallContent("2", "search_places", new Dictionary<string, object> { ["query"] = "pharmacy", ["near"] = "Ottawa" })),
            Is.EqualTo("Looking for “pharmacy” in Ottawa"));
    }

#if TEST_MODELS
    // OpenStreetMap's Nominatim for real (needs the network; opt in with -p:TestModels=true): a shop name and a kind of
    // shop around Richmond Hill, Ontario, and a named town.
    [Test, Category("Network")]
    public async Task OpenStreetMapFindsPlacesForRealRequests()
    {
        PlaceSearchService service = new PlaceSearchService(new OpenStreetMapPlaceSearch(), deviceLocation: _ => Task.FromResult(Home));
        foreach (string query in new[] { "Tim Hortons", "pharmacy", "Halloween store" })
        {
            string text = await service.SearchAsync(query, "", CancellationToken.None);
            Console.WriteLine("== {0}\n{1}", query, text);
            Assert.That(text, Does.Not.StartWith("The place search failed"));
        }
        string ottawa = await service.SearchAsync("Parliament Hill", "Ottawa", CancellationToken.None);
        Console.WriteLine("== Parliament Hill in Ottawa\n{0}", ottawa);
        Assert.That(ottawa, Does.Contain("near Ottawa"));
    }
#endif

    [Test]
    public void AMapLinkSearchesTheNameAndAddressOrTheCoordinates()
    {
        Assert.That(PlaceSearchService.MapLink(new PlaceResult { Name = "Shop & Co", Address = "1 Main St", Latitude = 1, Longitude = 2 }),
            Is.EqualTo("https://www.google.com/maps/search/?api=1&query=Shop%20%26%20Co%2C%201%20Main%20St"));
        Assert.That(PlaceSearchService.MapLink(new PlaceResult { Name = "Shop", Latitude = 43.88, Longitude = -79.44 }),
            Is.EqualTo("https://www.google.com/maps/search/?api=1&query=43.880000%2C-79.440000"));
    }

    [Test]
    public void DistancesAreGreatCircleKilometres()
    {
        Assert.That(PlaceGeometry.DistanceKm(43.88, -79.44, 43.89, -79.44), Is.EqualTo(1.11).Within(0.01));
        // Richmond Hill to Ottawa is about 360 km in a straight line.
        Assert.That(PlaceGeometry.DistanceKm(43.88, -79.44, 45.42, -75.70), Is.EqualTo(360).Within(20));
    }

    private sealed class Fake : IPlaceSearch
    {
        private readonly Func<string, double, double, IReadOnlyList<PlaceResult>> _search;

        public Fake(string name, Func<string, double, double, IReadOnlyList<PlaceResult>> search)
        {
            Name = name;
            _search = search;
        }

        public string Name { get; }

        public Task<IReadOnlyList<PlaceResult>> SearchAsync(string query, double latitude, double longitude, CancellationToken cancellationToken) =>
            Task.FromResult(_search(query, latitude, longitude));
    }
}
