using ServidorWeb.Modelos;

namespace ServidorWeb;

// STUB provisorio de P2. Lo mínimo para que P1 funcione.
// FALTA (lo agrega P2): validación anti path-traversal (../, %2e%2e, rutas absolutas).
public static class GestorArchivos
{
    public static Recurso? Obtener(string rutaUrl)
    {
        // Consigna: si la URL no pide archivo ("/" o "/carpeta/"), servimos index.html.
        if (string.IsNullOrEmpty(rutaUrl) || rutaUrl.EndsWith('/'))
            rutaUrl += "index.html";

        // Sacamos la '/' inicial ANTES de combinar: si el 2do argumento de Path.Combine
        // empieza con '/', .NET descarta la carpeta pública (error clásico de seguridad).
        string relativa = rutaUrl.TrimStart('/');

        string carpeta = Path.Combine(AppContext.BaseDirectory, Configuracion.CarpetaPublica);
        string rutaFisica = Path.Combine(carpeta, relativa);

        if (!File.Exists(rutaFisica))
            return null;

        return new Recurso
        {
            Contenido = File.ReadAllBytes(rutaFisica),
            ContentType = MimePorExtension(Path.GetExtension(rutaFisica))
        };
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
