namespace gatekeeper.Api;

public static class PaginasDoProduto
{
    public const string Showroom =
        """
        <!DOCTYPE html>
        <html lang="pt-BR">
        <body>
        <main>
          <h1>Showroom</h1>
          <button id="modelo-lead">Captura de lead</button>
          <button id="modelo-sdr">SDR</button>
          <button id="modelo-vendas">Vendas</button>
          <ol>
            <li id="passo-motor">Motor</li>
            <li id="passo-voz">Voz</li>
            <li id="passo-raciocinio">Raciocinio</li>
            <li id="passo-experimentar">Experimentar</li>
          </ol>
        </main>
        </body>
        </html>
        """;

    public const string Rack =
        """
        <!DOCTYPE html>
        <html lang="pt-BR">
        <body>
        <main id="rack">
          <h1>Rack de DSP</h1>
          <p id="preset">Voz</p>
          <label>Passa-altas <input id="hpf" value="100" /></label>
          <label>Passa-baixas <input id="lpf" value="3400" /></label>
          <label>Presenca <input id="presenca" value="2" /></label>
          <p id="ruido-estado">automatico</p>
          <p id="ruido-espectro">piso</p>
          <button id="bypass">Bypass</button>
          <button id="experimentar">Experimentar</button>
          <p id="erro">taxa de erro</p>
        </main>
        <script>
          document.getElementById("bypass").addEventListener("click", function () {
            document.getElementById("ruido-estado").textContent = "bypass";
          });
        </script>
        </body>
        </html>
        """;

    public const string Widget =
        """
        <!DOCTYPE html>
        <html lang="pt-BR">
        <body>
        <main id="widget">
          <h1>Player</h1>
          <button id="caminho-direto">PCM direto</button>
          <button id="caminho-g711">G.711</button>
          <button id="caminho-g729">G.729</button>
          <p id="caminho">direto</p>
          <p id="quadro">WER</p>
          <section id="referencias">
            <h2>Conversas de referência</h2>
            <button id="injetar-conjunto">Injetar conjunto</button>
            <ul id="provedores">
              <li data-provedor="elevenlabs">elevenlabs <span class="wer"></span></li>
              <li data-provedor="voice-live">voice-live <span class="wer"></span></li>
              <li data-provedor="openai">openai <span class="wer"></span></li>
              <li data-provedor="mistral">mistral <span class="wer"></span></li>
              <li data-provedor="mixtral">mixtral <span class="wer"></span></li>
            </ul>
          </section>
        </main>
        <script>
          function escolher(nome) {
            document.getElementById("caminho").textContent = nome;
          }
          document.getElementById("caminho-direto").addEventListener("click", function () { escolher("direto"); });
          document.getElementById("caminho-g711").addEventListener("click", function () { escolher("g711"); });
          document.getElementById("caminho-g729").addEventListener("click", function () { escolher("g729"); });
          document.getElementById("injetar-conjunto").addEventListener("click", function () {
            var itens = document.querySelectorAll("#provedores [data-provedor]");
            for (var i = 0; i < itens.length; i++) {
              itens[i].querySelector(".wer").textContent = "WER 0.1; desfecho concluida; primeiro audio; custo";
            }
            document.getElementById("quadro").textContent = "WER por provedor";
          });
        </script>
        </body>
        </html>
        """;
}
