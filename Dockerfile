FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY LngTool.csproj ./
RUN dotnet restore LngTool.csproj -r linux-x64

COPY . ./
RUN dotnet publish LngTool.csproj \
    -c Release \
    -f net9.0 \
    -r linux-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:DebugType=None \
    -p:DebugSymbols=false \
    -o /out

FROM mcr.microsoft.com/dotnet/runtime-deps:9.0
WORKDIR /work
COPY --from=build /out/ObsCureTextEditorCLI /usr/local/bin/ObsCureTextEditorCLI
ENTRYPOINT ["ObsCureTextEditorCLI"]
