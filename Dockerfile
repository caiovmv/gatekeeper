# gatekeeper (API). Contexto de build = esta pasta, e nao src/:
# e aqui que vivem Directory.Build.props, Directory.Packages.props e global.json.
# Sem eles o csproj nao tem TargetFramework nem versao de pacote, e o restore falha.
#
# Runtime (aspnet) e SDK versionam diferente: nao existe tag aspnet:10.0.402.
# O global.json pede 10.0.303 com rollForward latestFeature. A tag 10.0.303 nao
# foi publicada; 10.0.402 e o SDK desta maquina e satisfaz esse pedido.
ARG DOTNET_VERSION=10.0
ARG DOTNET_SDK_VERSION=10.0.402

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_SDK_VERSION} AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/gatekeeper.Api/gatekeeper.Api.csproj                             src/gatekeeper.Api/
COPY src/gatekeeper.Application/gatekeeper.Application.csproj             src/gatekeeper.Application/
COPY src/gatekeeper.Infrastructure/gatekeeper.Infrastructure.csproj       src/gatekeeper.Infrastructure/
COPY src/gatekeeper.Domain/gatekeeper.Domain.csproj                       src/gatekeeper.Domain/
RUN dotnet restore src/gatekeeper.Api/gatekeeper.Api.csproj
COPY . .
# Declarado DEPOIS do COPY . . de proposito: essa linha ja invalida o cache a cada
# mudanca de fonte, entao o ARG aqui nao custa o restore inteiro a cada commit.
ARG GIT_SHA=desconhecido
# /p:SourceRevisionId assa o commit no AssemblyInformationalVersion (1.0.0+<sha>).
RUN dotnet publish src/gatekeeper.Api/gatekeeper.Api.csproj -c Release -o /app /p:UseAppHost=false /p:SourceRevisionId=$GIT_SHA

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime
# ARG nao atravessa estagio: precisa ser redeclarado para o LABEL e o /app/COMMIT.
ARG GIT_SHA=desconhecido
ARG GIT_BRANCH=desconhecido
LABEL org.opencontainers.image.title="gatekeeper" \
      org.opencontainers.image.revision="$GIT_SHA" \
      org.opencontainers.image.version="$GIT_BRANCH"
# libssl/ca-certificates para SQL Server (TLS) e chamadas HTTPS. curl para o HEALTHCHECK.
RUN apt-get update \
 && apt-get install -y --no-install-recommends ca-certificates libssl3 curl \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app ./
# O marcador que se le com `docker exec ... cat /app/COMMIT`.
# Vem DEPOIS do COPY --from=build, senao o COPY apaga o arquivo.
RUN echo "$GIT_SHA" > /app/COMMIT

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true

EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=45s --retries=3 \
  CMD curl -fsS http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "gatekeeper.Api.dll"]
