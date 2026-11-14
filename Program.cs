using ServidorWeb;

Configuracion.Cargar();

// Fuente de cancelación: Ctrl+C la dispara y el servidor termina ordenadamente.
using var cts = new CancellationTokenSource();

Console.CancelKeyPress += (sender, e) =>
{
    // e.Cancel = true evita que el proceso muera de golpe:
    // dejamos que el servidor cierre el listener y espere las tareas en curso.
    e.Cancel = true;
    Console.WriteLine("[Program] Ctrl+C recibido; cerrando...");
    cts.Cancel();
};

var servidor = new Servidor(Configuracion.Puerto);
await servidor.IniciarAsync(cts.Token);
