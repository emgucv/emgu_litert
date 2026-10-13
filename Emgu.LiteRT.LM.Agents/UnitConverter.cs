//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Emgu.LiteRT.LM.Agents
{
    /// <summary>
    /// Converts between units of length, mass, volume, temperature, speed, area, time, data, energy and pressure, for
    /// the convert_units tool (AgentTools.ConvertUnits) - small language models get conversions wrong. Units are matched by
    /// symbol or name, singular or plural, in any case ("km", "kilometres", "mi", "miles", "°F", "fahrenheit", "fl oz",
    /// "cups"). US customary volumes (gallon, quart, pint, cup, fluid ounce); "imperial gallon" / "imperial pint" too.
    /// </summary>
    public static class UnitConverter
    {
        private sealed class Unit
        {
            public string Name;
            public string Kind;
            // value in base unit = (value + Offset) * Factor; only temperature has an offset.
            public double Factor;
            public double Offset;
        }

        private static readonly Dictionary<string, Unit> Units = new Dictionary<string, Unit>(StringComparer.OrdinalIgnoreCase);

        static UnitConverter()
        {
            // Length (metre)
            Add("length", 1, "m", "metre", "meter");
            Add("length", 1000, "km", "kilometre", "kilometer");
            Add("length", 0.01, "cm", "centimetre", "centimeter");
            Add("length", 0.001, "mm", "millimetre", "millimeter");
            Add("length", 1e-6, "µm", "um", "micrometre", "micrometer", "micron");
            Add("length", 1609.344, "mi", "mile");
            Add("length", 0.9144, "yd", "yard");
            Add("length", 0.3048, "ft", "foot", "feet");
            Add("length", 0.0254, "in", "inch", "inches");
            Add("length", 1852, "nmi", "nautical mile");
            // Mass (kilogram)
            Add("mass", 1, "kg", "kilogram", "kilo");
            Add("mass", 0.001, "g", "gram", "gramme");
            Add("mass", 1e-6, "mg", "milligram");
            Add("mass", 1000, "t", "tonne", "metric ton");
            Add("mass", 0.45359237, "lb", "lbs", "pound");
            Add("mass", 0.028349523125, "oz", "ounce");
            Add("mass", 6.35029318, "st", "stone");
            Add("mass", 907.18474, "short ton", "us ton");
            // Volume (litre)
            Add("volume", 1, "l", "litre", "liter");
            Add("volume", 0.001, "ml", "millilitre", "milliliter");
            Add("volume", 0.01, "cl", "centilitre", "centiliter");
            Add("volume", 0.1, "dl", "decilitre", "deciliter");
            Add("volume", 1000, "m3", "m³", "cubic metre", "cubic meter");
            Add("volume", 0.001, "cm3", "cm³", "cc", "cubic centimetre", "cubic centimeter");
            Add("volume", 3.785411784, "gal", "gallon", "us gallon");
            Add("volume", 4.54609, "imperial gallon", "uk gallon");
            Add("volume", 0.946352946, "qt", "quart");
            Add("volume", 0.473176473, "pt", "pint", "us pint");
            Add("volume", 0.56826125, "imperial pint", "uk pint");
            Add("volume", 0.2365882365, "cup");
            Add("volume", 0.0295735295625, "fl oz", "floz", "fluid ounce");
            Add("volume", 0.01478676478125, "tbsp", "tablespoon");
            Add("volume", 0.00492892159375, "tsp", "teaspoon");
            // Temperature (kelvin): K = (value + offset) * factor
            AddTemperature("c", 273.15, 1, "°c", "celsius", "centigrade", "degree celsius", "degrees celsius");
            AddTemperature("f", 459.67, 5.0 / 9, "°f", "fahrenheit", "degree fahrenheit", "degrees fahrenheit");
            AddTemperature("k", 0, 1, "kelvin");
            // Speed (metre per second)
            Add("speed", 1, "m/s", "metre per second", "meter per second", "mps");
            Add("speed", 1000.0 / 3600, "km/h", "kmh", "kph", "kilometre per hour", "kilometer per hour", "kilometres per hour", "kilometers per hour");
            Add("speed", 1609.344 / 3600, "mph", "mile per hour", "miles per hour");
            Add("speed", 1852.0 / 3600, "kn", "knot", "kt");
            Add("speed", 0.3048, "ft/s", "fps", "foot per second", "feet per second");
            // Area (square metre)
            Add("area", 1, "m2", "m²", "sq m", "square metre", "square meter");
            Add("area", 1e6, "km2", "km²", "sq km", "square kilometre", "square kilometer");
            Add("area", 1e-4, "cm2", "cm²", "square centimetre", "square centimeter");
            Add("area", 10000, "ha", "hectare");
            Add("area", 4046.8564224, "acre", "ac");
            Add("area", 0.09290304, "ft2", "ft²", "sq ft", "square foot", "square feet");
            Add("area", 0.83612736, "yd2", "yd²", "sq yd", "square yard");
            Add("area", 2589988.110336, "mi2", "mi²", "sq mi", "square mile");
            // Time (second)
            Add("time", 1, "s", "sec", "second");
            Add("time", 0.001, "ms", "millisecond");
            Add("time", 60, "min", "minute");
            Add("time", 3600, "h", "hr", "hour");
            Add("time", 86400, "d", "day");
            Add("time", 604800, "wk", "week");
            Add("time", 31557600, "yr", "year");
            // Data (byte; kB = 1000 B, KiB = 1024 B)
            Add("data", 1, "B", "byte");
            Add("data", 0.125, "bit");
            Add("data", 1e3, "kB", "kilobyte");
            Add("data", 1e6, "MB", "megabyte");
            Add("data", 1e9, "GB", "gigabyte");
            Add("data", 1e12, "TB", "terabyte");
            Add("data", 1024, "KiB", "kibibyte");
            Add("data", 1048576, "MiB", "mebibyte");
            Add("data", 1073741824, "GiB", "gibibyte");
            Add("data", 1099511627776, "TiB", "tebibyte");
            // Energy (joule)
            Add("energy", 1, "J", "joule");
            Add("energy", 1000, "kJ", "kilojoule");
            Add("energy", 4.184, "cal", "calorie");
            Add("energy", 4184, "kcal", "kilocalorie", "food calorie");
            Add("energy", 3600, "Wh", "watt hour", "watt-hour");
            Add("energy", 3.6e6, "kWh", "kilowatt hour", "kilowatt-hour");
            Add("energy", 1055.05585262, "BTU", "british thermal unit");
            // Pressure (pascal)
            Add("pressure", 1, "Pa", "pascal");
            Add("pressure", 1000, "kPa", "kilopascal");
            Add("pressure", 100, "hPa", "hectopascal", "mbar", "millibar");
            Add("pressure", 100000, "bar");
            Add("pressure", 6894.757293168, "psi", "pound per square inch", "pounds per square inch");
            Add("pressure", 101325, "atm", "atmosphere");
            Add("pressure", 133.322387415, "mmHg", "millimetre of mercury", "millimeter of mercury");
        }

        private static void Add(string kind, double factor, string symbol, params string[] names)
        {
            Unit unit = new Unit { Name = symbol, Kind = kind, Factor = factor };
            Units[symbol] = unit;
            foreach (string name in names)
                Units[name] = unit;
        }

        private static void AddTemperature(string symbol, double offset, double factor, params string[] names)
        {
            Unit unit = new Unit { Name = "°" + symbol.ToUpperInvariant(), Kind = "temperature", Factor = factor, Offset = offset };
            if (symbol == "k")
                unit.Name = "K";
            Units[symbol] = unit;
            foreach (string name in names)
                Units[name] = unit;
        }

        private static Unit Find(string text)
        {
            string key = (text ?? string.Empty).Trim().Replace("degrees ", "").Replace("degree ", "");
            if (key.Length == 0)
                return null;
            // Case matters only for the data units (MB vs mB is never meant; but "Mb" could be megabit - treated as MB).
            if (Units.TryGetValue(key, out Unit unit))
                return unit;
            if (Units.TryGetValue("degree " + key, out unit))
                return unit;
            // Plurals: "miles", "inches", "kilometres", "pounds per square inch" (handled above), "feet" (an alias).
            foreach (string singular in new[] { key.EndsWith("es") ? key.Substring(0, key.Length - 2) : null, key.EndsWith("s") ? key.Substring(0, key.Length - 1) : null })
                if (singular != null && Units.TryGetValue(singular, out unit))
                    return unit;
            return null;
        }

        /// <summary>
        /// Convert a value.
        /// </summary>
        /// <param name="value">The value</param>
        /// <param name="from">The unit it is in, e.g. "miles"</param>
        /// <param name="to">The unit to convert to, e.g. "km"</param>
        /// <returns>The converted value</returns>
        /// <exception cref="ArgumentException">A unit isn't known, or the two measure different things</exception>
        public static double Convert(double value, string from, string to)
        {
            Unit source = Find(from);
            Unit target = Find(to);
            if (source == null)
                throw new ArgumentException(string.Format(CultureInfo.InvariantCulture, "Unknown unit \"{0}\"", from));
            if (target == null)
                throw new ArgumentException(string.Format(CultureInfo.InvariantCulture, "Unknown unit \"{0}\"", to));
            if (source.Kind != target.Kind)
                throw new ArgumentException(string.Format(CultureInfo.InvariantCulture,
                    "Can't convert {0} ({1}) to {2} ({3})", from, source.Kind, to, target.Kind));
            double baseValue = (value + source.Offset) * source.Factor;
            return baseValue / target.Factor - target.Offset;
        }

        /// <summary>
        /// The standard symbol of a unit, e.g. "km" for "kilometres", or null if it isn't known.
        /// </summary>
        public static string Symbol(string unit)
        {
            return Find(unit)?.Name;
        }

        /// <summary>
        /// What a unit measures ("length", "mass", "volume", "temperature", "speed", "area", "time", "data", "energy",
        /// "pressure"), or null if it isn't known.
        /// </summary>
        public static string Kind(string unit)
        {
            return Find(unit)?.Kind;
        }

        /// <summary>
        /// The kinds of quantity and some of their units, e.g. for a tool description.
        /// </summary>
        public static string Supported
        {
            get
            {
                return string.Join("; ", Units.Values.Distinct().GroupBy(u => u.Kind)
                    .Select(g => g.Key + ": " + string.Join(", ", g.Select(u => u.Name))));
            }
        }
    }
}
