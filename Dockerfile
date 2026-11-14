FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish Bookstore/Bookstore.csproj -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /out .
ENV ASPNETCORE_HTTP_PORTS=8080
ENV Bookstore__Database=/data/bookstore.db
EXPOSE 8080
ENTRYPOINT ["dotnet", "Bookstore.dll"]
