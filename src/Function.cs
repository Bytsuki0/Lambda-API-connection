using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using Amazon.Lambda.Core;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Serialization.SystemTextJson;

// Enable Lambda JSON serialization
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace OpenMeteoLambda
{
    public class Function
    {   
        
        private static readonly HttpClient client = new HttpClient();
        
        private static readonly string BaseUrl = "https://api.open-meteo.com/v1/forecast";

        public async Task<APIGatewayProxyResponse> FunctionHandler(APIGatewayProxyRequest request, ILambdaContext context)
        {
            // lat e lon podem ser nulos — usar string?
            string? lat = null;
            string? lon = null;

            // QueryStringParameters pode ser null; verificar antes de usar
            if (request?.QueryStringParameters != null)
            {
                request.QueryStringParameters.TryGetValue("lat", out lat);
                request.QueryStringParameters.TryGetValue("lon", out lon);
            }

            if (string.IsNullOrWhiteSpace(lat) || string.IsNullOrWhiteSpace(lon))
            {
                return new APIGatewayProxyResponse
                {
                    StatusCode = 400,
                    Body = "Missing 'lat' or 'lon'",
                    Headers = new Dictionary<string, string> { { "Content-Type", "text/plain" } }
                };
            }

            // 2) Validar se são numéricas
            if (!double.TryParse(lat, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out double latNum))
            {
                return new APIGatewayProxyResponse
                {
                    StatusCode = 400,
                    Body = "Latitude must be numeric.",
                    Headers = new Dictionary<string, string> { { "Content-Type", "text/plain" } }
                };
            }

            if (!double.TryParse(lon, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out double lonNum))
            {
                return new APIGatewayProxyResponse
                {
                    StatusCode = 400,
                    Body = "Longitude must be numeric.",
                    Headers = new Dictionary<string, string> { { "Content-Type", "text/plain" } }
                };
            }

            // 3) Validar limites geográficos
            if (latNum < -90 || latNum > 90)
            {
                return new APIGatewayProxyResponse
                {
                    StatusCode = 400,
                    Body = "Latitude must be between -90 and 90.",
                    Headers = new Dictionary<string, string> { { "Content-Type", "text/plain" } }
                };
            }

            if (lonNum < -180 || lonNum > 180)
            {
                return new APIGatewayProxyResponse
                {
                    StatusCode = 400,
                    Body = "Longitude must be between -180 and 180.",
                    Headers = new Dictionary<string, string> { { "Content-Type", "text/plain" } }
                };
            }

            string url = $"{BaseUrl}?latitude={lat}&longitude={lon}&current_weather=true";

            HttpResponseMessage response;
            try
            {
                response = await client.GetAsync(url);
            }
            catch (Exception ex)
            {
                // Erro de rede/HTTP
                return new APIGatewayProxyResponse
                {
                    StatusCode = 500,
                    Body = $"Error calling weather API: {ex.Message}",
                    Headers = new Dictionary<string, string> { { "Content-Type", "text/plain" } }
                };
            }

            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return new APIGatewayProxyResponse
                {
                    StatusCode = (int)response.StatusCode,
                    Body = json,
                    Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
                };
            }

            using JsonDocument doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("current_weather", out JsonElement root))
            {
                return new APIGatewayProxyResponse
                {
                    StatusCode = 502,
                    Body = "Unexpected response format from weather API (missing current_weather).",
                    Headers = new Dictionary<string, string> { { "Content-Type", "text/plain" } }
                };
            }

            var result = new
            {
                latitude = lat,
                longitude = lon,
                temperatura = root.GetProperty("temperature").GetDouble(),
                vento = root.GetProperty("windspeed").GetDouble(),
                hora = root.GetProperty("time").GetString()
            };

            string jsonOutput = JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            return new APIGatewayProxyResponse
            {
                StatusCode = 200,
                Body = jsonOutput,
                Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
            };
        }
    }
}

namespace OpenMeteoLambda.Tester
{
    class Program
    {
        // async Main para permitir await
        static async Task Main(string[] args)
        {
            // Permite passar lat/lon via argumentos: dotnet run -- 40.7128 -74.0060
            string lat = args.Length > 0 ? args[0] : "40.7128";
            string lon = args.Length > 1 ? args[1] : "-74.0060";

            var request = new APIGatewayProxyRequest
            {
                QueryStringParameters = new Dictionary<string, string>
                {
                    { "lat", lat },
                    { "lon", lon }
                }
            };

            var function = new Function();

            // Para testes locais podemos passar null como ILambdaContext se você não usar o contexto dentro da função
            var response = await function.FunctionHandler(request, null);

            Console.WriteLine("----- Response -----");
            Console.WriteLine($"StatusCode: {response.StatusCode}");
            Console.WriteLine("Headers:");
            if (response.Headers != null)
            {
                foreach (var kv in response.Headers)
                {
                    Console.WriteLine($"  {kv.Key}: {kv.Value}");
                }
            }
            Console.WriteLine("Body:");
            Console.WriteLine(response.Body);
            Console.WriteLine("--------------------");
        }
    }
}