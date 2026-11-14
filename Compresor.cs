using System.IO.Compression;

namespace ServidorWeb;

// STUB provisorio de P2, ya funcional.
// Contrato: Compresor.EsComprimible(contentType) y Compresor.ComprimirGzip(datos).
public static class Compresor
{
    // Solo comprimimos texto: imágenes/zip ya vienen comprimidos y gzip casi no gana.
    public static bool EsComprimible(string contentType)
    {
        if (string.IsNullOrEmpty(contentType))
            return false;

        // Nos quedamos con el tipo puro, sin parámetros como "; charset=utf-8".
        string tipo = contentType.Split(';')[0].Trim().ToLowerInvariant();

        return tipo.StartsWith("text/")
            || tipo is "application/json" or "application/xml" or "application/javascript"
            || tipo.EndsWith("+json")
            || tipo.EndsWith("+xml")
            || tipo == "image/svg+xml";
    }

    public static byte[] ComprimirGzip(byte[] datos)
    {
        using var salida = new MemoryStream();

        // leaveOpen: true → al cerrar el GZipStream no cierra el MemoryStream,
        // así podemos pedir ToArray() después.
        using (var gzip = new GZipStream(salida, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(datos, 0, datos.Length);
        }

        return salida.ToArray();
    }
}
