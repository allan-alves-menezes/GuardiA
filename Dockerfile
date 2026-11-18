# Estágio 1: Build da aplicação com o SDK do .NET 10
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copia os arquivos de projeto e restaura as dependências
COPY . .
RUN dotnet restore
RUN dotnet publish -c Release -o /app/publish

# Estágio 2: Imagem final leve apenas com o Runtime ASP.NET Core 10
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Configura a porta padrão para nuvem
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# Substitua GuardiA.dll pelo nome exato do seu projeto principal (.csproj)
ENTRYPOINT ["dotnet", "GuardiA.dll"]