using System.Text;

namespace ServidorWeb;

// STUB provisorio de P2, ya funcional y seguro entre hilos.
// Contrato: Logger.Registrar(ip, metodo, url, datos).
public static class Logger
{
    // readonly: la referencia nunca cambia, así TODOS los hilos usan la MISMA llave.
    // Si fuera un tipo por valor, cada hilo boxearía una copia y el lock no bloquearía nada.
    private static readonly object _lock = new();

    public static void Registrar(string ip, string metodo, string url, string datos)
    {
        try
        {
            string carpeta = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(carpeta); // idempotente: no falla si ya existe

            string archivo = Path.Combine(carpeta, $"{DateTime.Now:yyyy-MM-dd}.log");
            string linea =
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {ip} | {metodo} | {url} | {datos}{Environment.NewLine}";

            // lock: un solo hilo a la vez entra a esta sección crítica → líneas íntegras.
            lock (_lock)
            {
                File.AppendAllText(archivo, linea, Encoding.UTF8);
            }
        }
        catch (Exception ex)
        {
            // Un fallo de log no debe tirar abajo la solicitud del cliente.
            Console.WriteLine($"[Logger] No se pudo escribir el log: {ex.Message}");
        }
    }
}
