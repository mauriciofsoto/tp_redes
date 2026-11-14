using System.Net;
using System.Net.Sockets;
using System.Text;
using ServidorWeb.Modelos;

namespace ServidorWeb;

// Responsabilidad: orquestar una solicitud completa. Es el único que habla con el cliente
// y el que arma la respuesta HTTP; delega archivos/logs/compresión en las clases de P2.
public class ManejadorSolicitudes
{
    // Tiempo máximo para que el cliente termine de mandar TODA la solicitud.
    private static readonly TimeSpan TimeoutLectura = TimeSpan.FromSeconds(10);

    public async Task AtenderAsync(TcpClient cliente, CancellationToken token)
    {
        // using anidados: se liberan el stream y el socket aunque haya excepción.
        using (cliente)
        using (NetworkStream stream = cliente.GetStream())
        {
            string ip = ObtenerIp(cliente);

            // Token propio con timeout, enlazado al del servidor:
            // - se cancela si el cliente tarda demasiado (timeout)
            // - o si el servidor se apaga (token enlazado)
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(TimeoutLectura);

            try
            {
                Solicitud? solicitud = null;

                try
                {
                    solicitud = await AnalizadorHttp.LeerAsync(stream, ip, cts.Token);
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    // Timeout de lectura: NO fue apagado del servidor. Respondemos 408.
                    // Usamos "token" (no cts.Token) porque cts ya está cancelado.
                    await EnviarAsync(stream, 408, "Request Timeout", "text/plain; charset=utf-8",
                        Encoding.UTF8.GetBytes("Tiempo de espera agotado."), token);
                    return;
                }
                catch (SolicitudInvalidaException ex)
                {
                    // 400 o 413 según lo que haya detectado el analizador.
                    await EnviarAsync(stream, ex.StatusCode, Motivo(ex.StatusCode),
                        "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(ex.Message), token);
                    return;
                }
                catch (IOException)
                {
                    // El cliente cortó la conexión antes de mandar la solicitud.
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                // Conexión vacía: no se loguea y no se responde, solo se cierra.
                if (solicitud is null)
                    return;

                // La URL se loguea tal cual llegó: ruta + query string.
                string url = solicitud.Ruta +
                    (solicitud.QueryString.Length > 0 ? "?" + solicitud.QueryString : "");
                string datos = solicitud.Metodo == "POST" ? solicitud.Cuerpo : "";
                Logger.Registrar(ip, solicitud.Metodo, url, datos);

                await ProcesarAsync(stream, solicitud, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw; // apagado del servidor: que lo maneje Servidor
            }
            catch (Exception)
            {
                // Cualquier otro fallo: 500 "mejor esfuerzo"; el servidor sigue vivo.
                await EnviarAsync(stream, 500, "Internal Server Error", "text/plain; charset=utf-8",
                    Encoding.UTF8.GetBytes("Error interno del servidor."), token);
            }
        }
    }

    private static async Task ProcesarAsync(NetworkStream stream, Solicitud solicitud, CancellationToken token)
    {
        switch (solicitud.Metodo)
        {
            case "GET":
                await ResponderGetAsync(stream, solicitud, token);
                break;

            case "POST":
                // Consigna: en POST solo se loguean los datos (ya se loguearon arriba).
                await EnviarAsync(stream, 200, "OK", "text/plain; charset=utf-8",
                    Encoding.UTF8.GetBytes("POST recibido"), token);
                break;

            default:
                await EnviarAsync(stream, 405, "Method Not Allowed", "text/plain; charset=utf-8",
                    Encoding.UTF8.GetBytes($"Método {solicitud.Metodo} no permitido."), token,
                    headersExtra: "Allow: GET, POST\r\n");
                break;
        }
    }

    private static async Task ResponderGetAsync(NetworkStream stream, Solicitud solicitud, CancellationToken token)
    {
        Recurso? recurso = GestorArchivos.Obtener(solicitud.Ruta);

        if (recurso is null)
        {
            // 404 personalizado: lo pide P1 a P2; si tampoco existe, texto fijo mínimo.
            Recurso? pagina404 = GestorArchivos.Obtener("/404.html");
            byte[] cuerpo404 = pagina404?.Contenido
                ?? Encoding.UTF8.GetBytes("404 - Archivo no encontrado");
            string tipo404 = pagina404?.ContentType ?? "text/plain; charset=utf-8";

            await EnviarAsync(stream, 404, "Not Found", tipo404, cuerpo404, token);
            return;
        }

        byte[] cuerpo = recurso.Contenido;
        string contentType = recurso.ContentType;

        // Gzip solo si el cliente lo acepta, el tipo gana y el tamaño lo justifica.
        bool comprimir = AceptaGzip(solicitud)
            && Compresor.EsComprimible(contentType)
            && cuerpo.Length >= 1024;

        if (comprimir)
            cuerpo = Compresor.ComprimirGzip(cuerpo);

        await EnviarAsync(stream, 200, "OK", contentType, cuerpo, token, comprimido: comprimir);
    }

    private static bool AceptaGzip(Solicitud solicitud)
    {
        if (!solicitud.Headers.TryGetValue("Accept-Encoding", out string? valor) || valor is null)
            return false;

        // "gzip, deflate, br" → buscamos el token gzip.
        return valor.Split(',')
            .Any(parte => parte.Trim().StartsWith("gzip", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task EnviarAsync(
        NetworkStream stream,
        int codigo,
        string motivo,
        string contentType,
        byte[] cuerpo,
        CancellationToken token,
        bool comprimido = false,
        string headersExtra = "")
    {
        var cabecera = new StringBuilder();
        cabecera.Append($"HTTP/1.1 {codigo} {motivo}\r\n");
        cabecera.Append($"Content-Type: {contentType}\r\n");
        // Content-Length SIEMPRE del cuerpo final: si está comprimido, el tamaño comprimido.
        cabecera.Append($"Content-Length: {cuerpo.Length}\r\n");
        if (comprimido)
            cabecera.Append("Content-Encoding: gzip\r\n");
        cabecera.Append(headersExtra);
        cabecera.Append("Connection: close\r\n");
        cabecera.Append("\r\n");

        byte[] bytesCabecera = Encoding.ASCII.GetBytes(cabecera.ToString());

        try
        {
            await stream.WriteAsync(bytesCabecera.AsMemory(), token);
            if (cuerpo.Length > 0)
                await stream.WriteAsync(cuerpo.AsMemory(), token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw; // apagado del servidor
        }
        catch (IOException)
        {
            // Cliente desconectado durante la escritura: se ignora.
        }
        catch (ObjectDisposedException)
        {
            // Socket ya cerrado: se ignora.
        }
    }

    private static string ObtenerIp(TcpClient cliente) =>
        cliente.Client.RemoteEndPoint is IPEndPoint extremo
            ? extremo.Address.ToString()
            : "desconocida";

    private static string Motivo(int codigo) => codigo switch
    {
        400 => "Bad Request",
        404 => "Not Found",
        405 => "Method Not Allowed",
        408 => "Request Timeout",
        413 => "Payload Too Large",
        500 => "Internal Server Error",
        _ => "Error"
    };
}
