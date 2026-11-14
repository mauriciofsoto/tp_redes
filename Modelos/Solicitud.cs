namespace ServidorWeb.Modelos;

// Contenedor de datos (DTO) acordado con P2: el analizador lo llena y el resto lo lee.
public class Solicitud
{
    public string Metodo { get; set; } = "";
    public string Ruta { get; set; } = "/";
    public string QueryString { get; set; } = "";
    public string Cuerpo { get; set; } = "";
    public string IpOrigen { get; set; } = "";

    // OrdinalIgnoreCase: en HTTP los nombres de header no distinguen mayúsculas.
    public Dictionary<string, string> Headers { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}
