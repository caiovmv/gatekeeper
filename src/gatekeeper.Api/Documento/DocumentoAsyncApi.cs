namespace gatekeeper.Api;

public static class DocumentoAsyncApi
{
    public const string Json = """
        {
          "asyncapi": "3.0.0",
          "info": { "title": "gatekeeper", "version": "v1" },
          "channels": {
            "eco": { "address": "/ws/eco" },
            "chamada": { "address": "/ws/chamada" }
          }
        }
        """;

    public const string Html = """
        <!DOCTYPE html>
        <html lang="pt">
        <head><meta charset="utf-8"><title>gatekeeper AsyncAPI</title></head>
        <body>
        <pre id="saida"></pre>
        <script src="/asyncapi/cliente.js"></script>
        </body>
        </html>
        """;

    public const string Script = """
        const saida = document.getElementById("saida");
        const socket = new WebSocket((location.protocol === "https:" ? "wss://" : "ws://") + location.host + "/ws/eco");
        socket.onopen = () => socket.send("oi");
        socket.onmessage = (evento) => { saida.textContent = evento.data; };
        """;
}
