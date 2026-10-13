//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace Emgu.LiteRT.LM.Agents
{
    /// <summary>
    /// A named place: a town with its region and country.
    /// </summary>
    public class Place
    {
        /// <summary>The town</summary>
        public String Town { get; internal set; }

        /// <summary>The region (state, province...), or null</summary>
        public String Region { get; internal set; }

        /// <summary>The country, or null</summary>
        public String Country { get; internal set; }

        /// <summary>The ISO 3166 country code, e.g. "FR"</summary>
        public String CountryCode { get; internal set; }

        /// <summary>The town's position</summary>
        public double Latitude { get; internal set; }

        /// <summary>The town's position</summary>
        public double Longitude { get; internal set; }

        /// <summary>How far the looked-up position is from the town, in km</summary>
        public double DistanceKm { get; internal set; }

        /// <summary>The town's population, or 0 if not known</summary>
        public long Population { get; internal set; }

        /// <summary>
        /// "Town, Region, Country", leaving out a region named like the town
        /// </summary>
        public override String ToString()
        {
            List<String> parts = new List<String> { Town };
            if (!String.IsNullOrEmpty(Region) && !String.Equals(Region, Town, StringComparison.OrdinalIgnoreCase))
                parts.Add(Region);
            if (!String.IsNullOrEmpty(Country))
                parts.Add(Country);
            return String.Join(", ", parts);
        }
    }

    /// <summary>
    /// Offline reverse geocoding: the nearest town to a position, from a list of towns loaded once - nothing is sent
    /// anywhere. The Emgu LiteRT MAUI demo ships GeoNames' towns of 5,000+ people (https://www.geonames.org, CC BY 4.0;
    /// built by its tools/make_places.py) as a gzip-compressed, tab-separated file: "C, country code, country name",
    /// "A, CC.admin1 code, region name" and "P, name, latitude, longitude, country code, admin1 code[, population]" lines.
    /// The nearest town wins, unless a bigger town is almost as close: a big city's districts are towns of their own
    /// (GeoNames' "Paris 16 Passy" is 1.4 km from the Eiffel Tower, central Paris 4.4 km).
    /// </summary>
    public class PlaceLookup
    {
        private readonly List<Place> _places = new List<Place>();
        // The places in each 1 x 1 degree cell, by (floor(latitude), floor(longitude)).
        private readonly Dictionary<long, List<Place>> _grid = new Dictionary<long, List<Place>>();

        /// <summary>
        /// The number of towns
        /// </summary>
        public int Count
        {
            get { return _places.Count; }
        }

        /// <summary>
        /// Load the towns from a stream of the file described above.
        /// </summary>
        /// <param name="stream">The file's content</param>
        /// <param name="gzipped">True if the stream is gzip-compressed</param>
        public static PlaceLookup Load(Stream stream, bool gzipped = true)
        {
            PlaceLookup lookup = new PlaceLookup();
            Dictionary<String, String> countries = new Dictionary<String, String>();
            Dictionary<String, String> regions = new Dictionary<String, String>();
            using (Stream input = gzipped ? new GZipStream(stream, CompressionMode.Decompress) : stream)
            using (StreamReader reader = new StreamReader(input, Encoding.UTF8))
            {
                String line;
                while ((line = reader.ReadLine()) != null)
                {
                    String[] f = line.Split('\t');
                    if (f.Length >= 3 && f[0] == "C")
                        countries[f[1]] = f[2];
                    else if (f.Length >= 3 && f[0] == "A")
                        regions[f[1]] = f[2];
                    else if (f.Length >= 6 && f[0] == "P"
                        && Double.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double lat)
                        && Double.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double lon))
                    {
                        countries.TryGetValue(f[4], out String country);
                        regions.TryGetValue(f[4] + "." + f[5], out String region);
                        long population = 0;
                        if (f.Length >= 7)
                            Int64.TryParse(f[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out population);
                        lookup.Add(new Place { Town = f[1], Latitude = lat, Longitude = lon, CountryCode = f[4], Country = country, Region = region, Population = population });
                    }
                }
            }
            return lookup;
        }

        private void Add(Place place)
        {
            _places.Add(place);
            long key = Key((int)Math.Floor(place.Latitude), (int)Math.Floor(place.Longitude));
            if (!_grid.TryGetValue(key, out List<Place> cell))
                _grid[key] = cell = new List<Place>();
            cell.Add(place);
        }

        private static long Key(int latitude, int longitude)
        {
            // Longitude wraps around the date line.
            longitude = ((longitude + 180) % 360 + 360) % 360 - 180;
            return (long)latitude * 1000 + longitude;
        }

        /// <summary>
        /// The nearest town to a position, or null if there is none within maxDistanceKm.
        /// </summary>
        /// <param name="latitude">The latitude, in degrees</param>
        /// <param name="longitude">The longitude, in degrees</param>
        /// <param name="maxDistanceKm">How far the town may be</param>
        public Place FindNearest(double latitude, double longitude, double maxDistanceKm = 75)
        {
            if (Double.IsNaN(latitude) || Double.IsNaN(longitude) || Math.Abs(latitude) > 90 || Math.Abs(longitude) > 180)
                return null;
            // Search the cells around the position; a degree of latitude is ~111 km, of longitude less away from the
            // equator, so wider rings of cells are searched until the radius is covered.
            int rings = Math.Min(30, (int)Math.Ceiling(maxDistanceKm / (111.0 * Math.Max(0.1, Math.Cos(latitude * Math.PI / 180)))) + 1);
            int cellLat = (int)Math.Floor(latitude);
            int cellLon = (int)Math.Floor(longitude);
            List<KeyValuePair<Place, double>> near = new List<KeyValuePair<Place, double>>();
            double nearest = Double.MaxValue;
            for (int dLat = -rings; dLat <= rings; dLat++)
                for (int dLon = -rings; dLon <= rings; dLon++)
                {
                    if (!_grid.TryGetValue(Key(cellLat + dLat, cellLon + dLon), out List<Place> cell))
                        continue;
                    foreach (Place place in cell)
                    {
                        double distance = DistanceKm(latitude, longitude, place.Latitude, place.Longitude);
                        if (distance > maxDistanceKm)
                            continue;
                        near.Add(new KeyValuePair<Place, double>(place, distance));
                        nearest = Math.Min(nearest, distance);
                    }
                }
            if (near.Count == 0)
                return null;
            // The biggest town about as close as the nearest one.
            double radius = 2 * nearest + 3;
            KeyValuePair<Place, double> chosen = near.Where(p => p.Value <= radius)
                .OrderByDescending(p => p.Key.Population).ThenBy(p => p.Value).First();
            Place best = chosen.Key;
            double bestDistance = chosen.Value;
            return new Place
            {
                Town = best.Town,
                Region = best.Region,
                Country = best.Country,
                CountryCode = best.CountryCode,
                Latitude = best.Latitude,
                Longitude = best.Longitude,
                DistanceKm = bestDistance,
                Population = best.Population
            };
        }

        /// <summary>
        /// The great-circle distance between two positions, in km.
        /// </summary>
        public static double DistanceKm(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371.0088;
            double dLat = (lat2 - lat1) * Math.PI / 180;
            double dLon = (lon2 - lon1) * Math.PI / 180;
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            return 2 * R * Math.Asin(Math.Min(1, Math.Sqrt(a)));
        }

        /// <summary>
        /// Whether a place name ("Paris, Île-de-France, France") contains a query ("paris", "ile de france", "France"),
        /// ignoring case, accents and punctuation.
        /// </summary>
        public static bool Matches(String place, String query)
        {
            if (String.IsNullOrWhiteSpace(place) || String.IsNullOrWhiteSpace(query))
                return false;
            return (" " + Fold(place) + " ").Contains(" " + Fold(query) + " ");
        }

        // Lower case, accents removed, anything but letters and digits a single space.
        private static String Fold(String text)
        {
            StringBuilder sb = new StringBuilder();
            bool space = false;
            foreach (char c in text.Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                    continue;
                if (Char.IsLetterOrDigit(c))
                {
                    if (space && sb.Length > 0)
                        sb.Append(' ');
                    space = false;
                    sb.Append(Char.ToLowerInvariant(c));
                }
                else
                {
                    space = true;
                }
            }
            return sb.ToString();
        }
    }
}
