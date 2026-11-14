
using ServidorWeb.Modelos;

namespace ServidorWeb;

public static class GestorArchivos
{
    public static Recurso? Obtener(string rutaUrl)
    {
        try
        {
            // Decodificamos la URL una sola vez.
            string rutaDecodificada = Uri.UnescapeDataString(rutaUrl ?? "");

            // Normalizamos las barras invertidas.
            rutaDecodificada = rutaDecodificada.Replace('\\', '/');

            // Si no se especifica un archivo, buscamos index.html.
            if (string.IsNullOrEmpty(rutaDecodificada) || rutaDecodificada.EndsWith('/'))
                rutaDecodificada += "index.html";

            // Eliminamos las barras iniciales.
            string relativa = rutaDecodificada.TrimStart('/');

            // Obtenemos la ruta absoluta de la carpeta pública.
            string carpeta = Path.GetFullPath(
                Path.Combine(AppContext.BaseDirectory, Configuracion.CarpetaPublica));

            // Construimos la ruta física solicitada.
            string rutaFisica = Path.GetFullPath(
                Path.Combine(carpeta, relativa.Replace('/', Path.DirectorySeparatorChar)));

            // Comprobamos que el archivo esté dentro de publico.
            string prefijoCarpeta = Path.EndsInDirectorySeparator(carpeta)
                ? carpeta
                : carpeta + Path.DirectorySeparatorChar;

            StringComparison comparacion = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            if (!rutaFisica.StartsWith(prefijoCarpeta, comparacion))
                return null;

            if (!File.Exists(rutaFisica))
                return null;

            return new Recurso
            {
                Contenido = File.ReadAllBytes(rutaFisica),
                ContentType = MimePorExtension(Path.GetExtension(rutaFisica))
            };
        }
        catch (Exception ex) when (
            ex is UriFormatException
            or ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            return null;
        }
    }

    private static string MimePorExtension(string extension) =>
        extension.ToLowerInvariant() switch
        {
            ".html" or ".htm" => "text/html; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".js" => "text/javascript; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".txt" => "text/plain; charset=utf-8",
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".ico" => "image/x-icon",
            ".pdf" => "application/pdf",
            ".zip" => "application/zip",
            _ => "application/octet-stream"
        };
}
