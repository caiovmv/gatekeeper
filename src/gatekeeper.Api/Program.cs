using gatekeeper.Api;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(opcoes =>
{
    opcoes.AddServerHeader = false;
    opcoes.ConfigureEndpointDefaults(extremidade =>
    {
        extremidade.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1AndHttp2;
    });
});

builder.Services.AddGrpc();
builder.Services.AddEsqueleto(builder.Configuration);
builder.Services.AddCompressao();
builder.Services.AddOpenApi(opcoes =>
{
    opcoes.AddDocumentTransformer<TransformadorDeSeguranca>();
});

var app = builder.Build();

app.UseCorrelacao();
app.UseCabecalhosDeSeguranca();
app.UseCompressao();
app.UseCorsDaMarca();
app.UseAutenticacaoPorChave();
app.UseCsrfDaSessao();
app.UseDigestDaPolitica();
app.UseLimiteDeMutacao();
app.UseLimiteDeOrigem();
app.UseTratamentoDeExcecao();
app.UseCacheDeLeitura();
app.UseWebSockets();
app.UseSwaggerUI(opcoes =>
{
    opcoes.RoutePrefix = "swagger";
    opcoes.SwaggerEndpoint("/openapi/v1.json", "gatekeeper");
});
app.UseReDoc(opcoes =>
{
    opcoes.RoutePrefix = "redoc";
    opcoes.SpecUrl("/openapi/v1.json");
});
app.MapOpenApi();
app.MapearRotas();
app.MapControllers();
app.MapGrpcService<gatekeeper.Api.Ingestao.ServicoDeIngestao>();

app.Run();
