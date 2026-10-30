//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

#if IOS || MACCATALYST
using CoreLocation;
using Emgu.LiteRT.LM.Agents;
using MapKit;

namespace Emgu.LiteRT.LM.Maui
{
    /// <summary>
    /// Place search with Apple Maps (MapKit's <c>MKLocalSearch</c>, on iOS and Mac Catalyst): no key and no permission of its
    /// own, a natural-language query over a region of about 60 km around the position. It has no ratings or opening hours
    /// here. The query and the position go to Apple.
    /// </summary>
    public sealed class AppleMapsPlaceSearch : IPlaceSearch
    {
        /// <inheritdoc/>
        public string Name => "Apple Maps";

        /// <inheritdoc/>
        public Task<IReadOnlyList<PlaceResult>> SearchAsync(string query, double latitude, double longitude, CancellationToken cancellationToken)
        {
            // MapKit's search is started from the main thread.
            return MainThread.InvokeOnMainThreadAsync<IReadOnlyList<PlaceResult>>(async () =>
            {
                MKLocalSearchRequest request = new MKLocalSearchRequest
                {
                    NaturalLanguageQuery = query,
                    Region = MKCoordinateRegion.FromDistance(new CLLocationCoordinate2D(latitude, longitude),
                        PlaceGeometry.SearchRadiusKm * 2000, PlaceGeometry.SearchRadiusKm * 2000)
                };
                MKLocalSearch search = new MKLocalSearch(request);
                using (cancellationToken.Register(() => search.Cancel()))
                {
                    MKLocalSearchResponse response = await search.StartAsync();
                    List<PlaceResult> results = new List<PlaceResult>();
                    foreach (MKMapItem item in response.MapItems)
                    {
                        if (string.IsNullOrWhiteSpace(item.Name) || item.Placemark == null)
                            continue;
                        string street = string.Join(" ", new[] { item.Placemark.SubThoroughfare, item.Placemark.Thoroughfare }
                            .Where(part => !string.IsNullOrWhiteSpace(part)));
                        string address = string.Join(", ", new[] { street, item.Placemark.Locality, item.Placemark.AdministrativeArea }
                            .Where(part => !string.IsNullOrWhiteSpace(part)));
                        results.Add(new PlaceResult
                        {
                            Name = item.Name,
                            Address = address.Length > 0 ? address : null,
                            Latitude = item.Placemark.Coordinate.Latitude,
                            Longitude = item.Placemark.Coordinate.Longitude,
                            Phone = string.IsNullOrWhiteSpace(item.PhoneNumber) ? null : item.PhoneNumber,
                            Website = item.Url?.AbsoluteString
                        });
                    }
                    return results;
                }
            });
        }
    }
}
#endif
