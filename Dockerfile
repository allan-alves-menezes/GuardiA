# Estágio 1: Build da aplicação com o SDK do .NET 10
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copia todo o repositório
COPY . .

# Restaura e publica especificamente o projeto Web Blazor (GuardiA.Admin)
RUN dotnet restore "GuardiA.Admin/GuardiA.Admin.csproj"
RUN dotnet publish "GuardiA.Admin/GuardiA.Admin.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Estágio 2: Imagem final leve com o Runtime ASP.NET Core 10
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Configura a porta padrão exigida pelo Render
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "GuardiA.Admin.dll"]