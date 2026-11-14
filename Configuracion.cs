using System.Text.Json;

namespace ServidorWeb;

// STUB provisorio de P2. Ya lee el config.json real, así P1 puede trabajar sin esperarla.
// Contrato: Configuracion.Puerto (int) y Configuracion.CarpetaPublica (string).
public static class Configuracion
{
    public static int Puerto { get; private set; } = 8080;
    public static string CarpetaPublica { get; private set; } = "publico";

    // Clase auxiliar solo para deserializar: sus nombres coinciden con las claves del JSON.
    private class ConfigDto
    {
        public int Puerto { get; set; }
        public string CarpetaPublica { get; set; } = "";
    }

    public static void Cargar()
    {
        // "Junto al ejecutable": con dotnet run es bin/Debug/net10.0/, ahí copia el .csproj.
        string ruta = Path.Combine(AppContext.BaseDirectory, "config.json");

        if (!File.Exists(ruta))
        {
            Console.WriteLine($"[Config] No existe {ruta}. Uso valores por defecto (8080 / publico).");
            return;
        }

        try
        {
            // CaseInsensitive: acepta "puerto" o "Puerto" sin depender de la capitalización.
            var opciones = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            ConfigDto? config = JsonSerializer.Deserialize<ConfigDto>(File.ReadAllText(ruta), opciones);

            if (config is null)
            {
                Console.WriteLine("[Config] config.json vacío. Uso valores por defecto.");
                return;
            }

            if (config.Puerto is > 0 and <= 65535)
                Puerto = config.Puerto;
            else
                Console.WriteLine($"[Config] Puerto inválido ({config.Puerto}). Uso {Puerto}.");

            if (!string.IsNullOrWhiteSpace(config.CarpetaPublica))
                CarpetaPublica = config.CarpetaPublica;
        }
        catch (Exception ex)
        {
            // Un config roto no debe impedir arrancar: avisamos y seguimos con los defaults.
            Console.WriteLine($"[Config] Error leyendo config.json: {ex.Message}. Uso valores por defecto.");
        }
    }
}
