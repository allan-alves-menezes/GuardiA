# Estágio 1: Build da aplicação unificada com o SDK do .NET 10
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copia todo o repositório
COPY . .

# Restaura e publica o projeto web consolidado (GuardiA.Web)
RUN dotnet restore "GuardiA.Web/GuardiA.Web.csproj"
RUN dotnet publish "GuardiA.Web/GuardiA.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Estágio 2: Imagem final leve com o Runtime ASP.NET Core 10
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Porta padrão (GuardiA.Web também respeita a variável PORT do Render em runtime)
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "GuardiA.Web.dll"]
