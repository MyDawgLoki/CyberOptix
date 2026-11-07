using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace CyberOptix.Services
{
    public record DayForecast(DateTime Date, double High, double Low, int Code);

    public record WeatherInfo(string Place, double Temp, double Feels, int Humidity, double Wind,
                              int Code, List<DayForecast> Days, bool Fahrenheit, double Uv = -1);   // Uv is -1 when the service did not give one

    /// <summary>Weather from Open-Meteo (free, no API key).</summary>
    public static class WeatherService
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

        public static async Task<WeatherInfo> GetAsync(string city, bool fahrenheit)
        {
            var geoJson = await Http.GetStringAsync(
                $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(city)}&count=1");
            using var geo = JsonDocument.Parse(geoJson);
            if (!geo.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
                throw new InvalidOperationException($"City not found: {city}");

            var place = results[0];
            var lat = place.GetProperty("latitude").GetDouble().ToString(CultureInfo.InvariantCulture);
            var lon = place.GetProperty("longitude").GetDouble().ToString(CultureInfo.InvariantCulture);
            var name = place.GetProperty("name").GetString() ?? city;

            var url = "https://api.open-meteo.com/v1/forecast" +
                      $"?latitude={lat}&longitude={lon}" +
                      "&current=temperature_2m,apparent_temperature,relative_humidity_2m,weather_code,wind_speed_10m,uv_index" +
                      "&daily=weather_code,temperature_2m_max,temperature_2m_min,uv_index_max&forecast_days=5&timezone=auto" +
                      $"&temperature_unit={(fahrenheit ? "fahrenheit" : "celsius")}" +
                      $"&wind_speed_unit={(fahrenheit ? "mph" : "kmh")}";

            using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
            var cur = doc.RootElement.GetProperty("current");
            var daily = doc.RootElement.GetProperty("daily");

            var days = new List<DayForecast>();
            var times = daily.GetProperty("time");
            for (int i = 0; i < times.GetArrayLength(); i++)
            {
                days.Add(new DayForecast(
                    DateTime.Parse(times[i].GetString()!, CultureInfo.InvariantCulture),
                    daily.GetProperty("temperature_2m_max")[i].GetDouble(),
                    daily.GetProperty("temperature_2m_min")[i].GetDouble(),
                    daily.GetProperty("weather_code")[i].GetInt32()));
            }

            // The UV index right now; if the service leaves it out, today's highest value is the next best thing.
            double uv = -1;
            if (cur.TryGetProperty("uv_index", out var uvNow) && uvNow.ValueKind == JsonValueKind.Number) uv = uvNow.GetDouble();
            else if (daily.TryGetProperty("uv_index_max", out var uvMax) && uvMax.GetArrayLength() > 0 && uvMax[0].ValueKind == JsonValueKind.Number) uv = uvMax[0].GetDouble();

            return new WeatherInfo(
                name,
                cur.GetProperty("temperature_2m").GetDouble(),
                cur.GetProperty("apparent_temperature").GetDouble(),
                (int)cur.GetProperty("relative_humidity_2m").GetDouble(),
                cur.GetProperty("wind_speed_10m").GetDouble(),
                cur.GetProperty("weather_code").GetInt32(),
                days,
                fahrenheit,
                uv);
        }

        public static (string Icon, string Text) Describe(int code) => code switch
        {
            0 => ("☀️", "Clear"),
            1 or 2 => ("⛅", "Partly cloudy"),
            3 => ("☁️", "Overcast"),
            45 or 48 => ("🌫️", "Fog"),
            >= 51 and <= 57 => ("🌦️", "Drizzle"),
            >= 61 and <= 67 => ("🌧️", "Rain"),
            >= 71 and <= 77 => ("❄️", "Snow"),
            >= 80 and <= 82 => ("🌧️", "Showers"),
            85 or 86 => ("🌨️", "Snow showers"),
            >= 95 => ("⛈️", "Thunderstorm"),
            _ => ("🌡️", "Unknown"),
        };
    }
}
