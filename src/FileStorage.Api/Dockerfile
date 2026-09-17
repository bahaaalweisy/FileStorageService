FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["src/FileStorage.Domain/FileStorage.Domain.csproj", "src/FileStorage.Domain/"]
COPY ["src/FileStorage.Application/FileStorage.Application.csproj", "src/FileStorage.Application/"]
COPY ["src/FileStorage.Infrastructure/FileStorage.Infrastructure.csproj", "src/FileStorage.Infrastructure/"]
COPY ["src/FileStorage.Api/FileStorage.Api.csproj", "src/FileStorage.Api/"]
RUN dotnet restore "src/FileStorage.Api/FileStorage.Api.csproj"

COPY src/FileStorage.Domain/ src/FileStorage.Domain/
COPY src/FileStorage.Application/ src/FileStorage.Application/
COPY src/FileStorage.Infrastructure/ src/FileStorage.Infrastructure/
COPY src/FileStorage.Api/ src/FileStorage.Api/

WORKDIR /src/src/FileStorage.Api
RUN dotnet publish -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

RUN useradd --uid 1654 --create-home appuser \
    && mkdir -p /app/_storage \
    && chown -R appuser:appuser /app

COPY --from=build /app/publish .
RUN chown -R appuser:appuser /app

USER appuser

ENV ASPNETCORE_URLS=http://+:8080
ENV Storage__RootPath=/app/_storage
EXPOSE 8080

ENTRYPOINT ["dotnet", "FileStorage.Api.dll"]
