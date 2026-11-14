namespace ServidorWeb.Modelos;

// Lo que devuelve GestorArchivos (P2): contenido del archivo y su tipo MIME.
// Si el archivo no existe, Obtener devuelve null (y eso se traduce a 404).
public class Recurso
{
    public byte[] Contenido { get; set; } = Array.Empty<byte>();
    public string ContentType { get; set; } = "application/octet-stream";
}
