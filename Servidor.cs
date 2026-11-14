using System.Net;
using System.Net.Sockets;

namespace ServidorWeb;

// Responsabilidad: escuchar el puerto, aceptar conexiones y lanzar una tarea por cliente.
// No sabe nada de HTTP ni de archivos: eso es de ManejadorSolicitudes.
public class Servidor
{
    private readonly int _puerto;

    // Semáforo: como máximo 100 solicitudes simultáneas. Es la "puerta" de concurrencia.
    private readonly SemaphoreSlim _limite = new(initialCount: 100);

    // Lista de tareas en curso. OJO: la toca SOLO el bucle de aceptación,
    // por eso es una List común y no necesita lock (no hay dos hilos a la vez acá).
    private readonly List<Task> _activas = new();

    // Manejador sin estado: una sola instancia compartida es segura entre hilos.
    private readonly ManejadorSolicitudes _manejador = new();

    public Servidor(int puerto) => _puerto = puerto;

    public async Task IniciarAsync(CancellationToken token)
    {
        // IPAddress.Any = escucha en todas las interfaces de red de la máquina.
        var listener = new TcpListener(IPAddress.Any, _puerto);
        listener.Start();
        Console.WriteLine($"[Servidor] Escuchando en http://localhost:{_puerto}/ (Ctrl+C para salir)");

        try
        {
            while (!token.IsCancellationRequested)
            {
                // 1) Pedimos cupo ANTES de aceptar: si ya hay 100 en curso, no aceptamos
                //    una conexión que no vamos a poder atender (queda en el backlog del SO).
                try
                {
                    await _limite.WaitAsync(token);
                }
                catch (OperationCanceledException)
                {
                    break; // Ctrl+C mientras esperábamos cupo: no hay nada que liberar
                }

                // 2) Aceptamos la conexión (asincrónico: no bloquea un hilo esperando).
                TcpClient cliente;
                try
                {
                    cliente = await listener.AcceptTcpClientAsync(token);
                }
                catch (OperationCanceledException)
                {
                    _limite.Release(); // habíamos tomado cupo: lo devolvemos
                    break;
                }
                catch (ObjectDisposedException)
                {
                    _limite.Release();
                    break; // listener.Stop()
                }
                catch (SocketException ex)
                {
                    _limite.Release();

                    if (token.IsCancellationRequested)
                        break; // apagado real

                    // Error pasajero al aceptar (ej. un cliente que resetea justo en ese momento):
                    // NO apagamos el servidor, seguimos escuchando.
                    Console.WriteLine($"[Servidor] Error al aceptar: {ex.Message}");
                    continue;
                }

                // 3) Lanzamos la tarea: corre en un hilo del ThreadPool (no creamos hilos).
                Task tarea = Task.Run(async () =>
                {
                    try
                    {
                        await _manejador.AtenderAsync(cliente, token);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        // Apagado normal: el token se canceló mientras se atendía al cliente.
                        // Lo capturamos aparte para no reportarlo como error.
                    }
                    catch (Exception ex)
                    {
                        // Red de seguridad: una excepción de un cliente no debe matar el bucle.
                        Console.WriteLine($"[Servidor] Error atendiendo cliente: {ex.Message}");
                    }
                    finally
                    {
                        _limite.Release(); // SIEMPRE devolvemos el cupo
                    }
                });

                // 4) Registramos la tarea. Antes sacamos las que ya terminaron
                //    para que la lista no crezca durante toda la vida del servidor.
                _activas.RemoveAll(t => t.IsCompleted);
                _activas.Add(tarea);
            }
        }
        finally
        {
            // 5) Apagado: liberamos el puerto y esperamos (con tope de 5 s) lo que esté en curso.
            listener.Stop();
            Console.WriteLine("[Servidor] Cerrando; esperando solicitudes en curso...");

            Task[] pendientes = _activas.ToArray();
            Task todas = Task.WhenAll(pendientes);
            await Task.WhenAny(todas, Task.Delay(TimeSpan.FromSeconds(5)));

            Console.WriteLine("[Servidor] Apagado.");
        }
    }
}
