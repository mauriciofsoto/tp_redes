using System.Net.Sockets;
using System.Text;
using ServidorWeb.Modelos;

namespace ServidorWeb;

// Se lanza cuando la solicitud no se puede interpretar.
// Lleva el código HTTP para que ManejadorSolicitudes sepa si responder 400 o 413.
public class SolicitudInvalidaException : Exception
{
    public int StatusCode { get; }

    public SolicitudInvalidaException(int statusCode, string mensaje) : base(mensaje)
    {
        StatusCode = statusCode;
    }
}

// Responsabilidad: convertir el texto HTTP crudo que llega por el socket en un objeto Solicitud.
public static class AnalizadorHttp
{
    private const int MaxBytesHeaders = 8 * 1024;   // 8 KB
    private const int MaxBytesCuerpo = 1024 * 1024; // 1 MB

    // Devuelve null si el cliente abrió la conexión y la cerró sin mandar ningún byte.
    public static async Task<Solicitud?> LeerAsync(NetworkStream stream, string ipOrigen, CancellationToken token)
    {
        // ---------- 1) Leer hasta el fin de headers: \r\n\r\n ----------
        var acumulado = new MemoryStream();
        byte[] buffer = new byte[4096];
        int finHeaders = -1;

        while (finHeaders < 0)
        {
            int leidos = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), token);

            if (leidos == 0)
            {
                // Cero bytes acumulados = conexión vacía (navegadores abren conexiones
                // especulativas y las cierran sin pedir nada). No es un error: no hay solicitud.
                if (acumulado.Length == 0)
                    return null;

                // Llegó algo pero se cortó antes de terminar los headers: eso sí es 400.
                throw new SolicitudInvalidaException(400, "La conexión se cerró antes de completar la solicitud.");
            }

            acumulado.Write(buffer, 0, leidos);

            byte[] datos = acumulado.GetBuffer();
            // Buscamos sobre TODO lo acumulado: el separador puede quedar partido entre dos lecturas.
            finHeaders = BuscarFinHeaders(datos, (int)acumulado.Length);

            // Si no apareció y ya nos pasamos del tope, la solicitud es abusiva.
            if (finHeaders < 0 && acumulado.Length > MaxBytesHeaders)
                throw new SolicitudInvalidaException(400, "Los headers superan el tamaño máximo.");
        }

        if (finHeaders > MaxBytesHeaders)
            throw new SolicitudInvalidaException(400, "Los headers superan el tamaño máximo.");

        byte[] todos = acumulado.GetBuffer();
        int totalBytes = (int)acumulado.Length;

        // Texto de headers (sin el \r\n\r\n final). Latin1 = 1 byte -> 1 carácter.
        string textoHeaders = Encoding.Latin1.GetString(todos, 0, finHeaders);

        // Bytes que ya vinieron pegados después de los headers: posible inicio del body.
        byte[] resto = new byte[totalBytes - (finHeaders + 4)];
        Array.Copy(todos, finHeaders + 4, resto, 0, resto.Length);

        // ---------- 2) Primera línea: METODO RUTA VERSION ----------
        string[] lineas = textoHeaders.Split("\r\n");
        if (lineas.Length == 0 || string.IsNullOrWhiteSpace(lineas[0]))
            throw new SolicitudInvalidaException(400, "Solicitud vacía.");

        string[] partes = lineas[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (partes.Length != 3 || !partes[2].StartsWith("HTTP/", StringComparison.OrdinalIgnoreCase))
            throw new SolicitudInvalidaException(400, "Línea de solicitud mal formada.");

        var solicitud = new Solicitud
        {
            Metodo = partes[0].ToUpperInvariant(),
            IpOrigen = ipOrigen
        };

        // Separar ruta y query string en el primer '?'.
        int signo = partes[1].IndexOf('?');
        if (signo >= 0)
        {
            solicitud.Ruta = partes[1][..signo];
            solicitud.QueryString = partes[1][(signo + 1)..];
        }
        else
        {
            solicitud.Ruta = partes[1];
        }

        if (string.IsNullOrEmpty(solicitud.Ruta))
            solicitud.Ruta = "/";

        // ---------- 3) Headers: "Nombre: valor" ----------
        for (int i = 1; i < lineas.Length; i++)
        {
            string linea = lineas[i];
            if (linea.Length == 0)
                continue;

            int dosPuntos = linea.IndexOf(':');
            if (dosPuntos <= 0)
                throw new SolicitudInvalidaException(400, "Header mal formado.");

            string nombre = linea[..dosPuntos].Trim();
            string valor = linea[(dosPuntos + 1)..].Trim();

            if (nombre.Length == 0)
                throw new SolicitudInvalidaException(400, "Header sin nombre.");

            // Si viene repetido, gana el último. Simple y suficiente para el TP.
            solicitud.Headers[nombre] = valor;
        }

        // ---------- 4) Body: leer exactamente Content-Length bytes ----------
        int longitudCuerpo = 0;
        if (solicitud.Headers.TryGetValue("Content-Length", out string? valorLongitud))
        {
            // TryParse evita excepciones por valores como "abc" o "-5".
            if (!int.TryParse(valorLongitud, out longitudCuerpo) || longitudCuerpo < 0)
                throw new SolicitudInvalidaException(400, "Content-Length inválido.");

            if (longitudCuerpo > MaxBytesCuerpo)
                throw new SolicitudInvalidaException(413, "El cuerpo supera el tamaño máximo.");
        }

        if (longitudCuerpo > 0)
        {
            byte[] cuerpo = new byte[longitudCuerpo];

            // Parte del body puede haber llegado junto con los headers: la copiamos primero.
            int copiados = Math.Min(resto.Length, longitudCuerpo);
            Array.Copy(resto, 0, cuerpo, 0, copiados);

            // Leemos lo que falte. Puede venir en varios pedazos, por eso el while.
            int faltan = longitudCuerpo - copiados;
            while (faltan > 0)
            {
                int leidos = await stream.ReadAsync(cuerpo.AsMemory(copiados, faltan), token);
                if (leidos == 0)
                    throw new SolicitudInvalidaException(400, "La conexión se cerró antes de completar el cuerpo.");

                copiados += leidos;
                faltan -= leidos;
            }

            solicitud.Cuerpo = Encoding.UTF8.GetString(cuerpo);
        }

        return solicitud;
    }

    // Busca la secuencia \r\n\r\n (13 10 13 10) y devuelve el índice donde empieza.
    private static int BuscarFinHeaders(byte[] datos, int cantidad)
    {
        for (int i = 0; i <= cantidad - 4; i++)
        {
            if (datos[i] == 13 && datos[i + 1] == 10 && datos[i + 2] == 13 && datos[i + 3] == 10)
                return i;
        }

        return -1;
    }
}
