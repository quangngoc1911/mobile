FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY FlowerShop.slnx ./
COPY src ./src
RUN dotnet restore src/FlowerShop.Api/FlowerShop.Api.csproj
RUN dotnet publish src/FlowerShop.Api/FlowerShop.Api.csproj -c Release -o /out --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
RUN mkdir -p /var/lib/flowershop/storage && chown -R $APP_UID:$APP_UID /var/lib/flowershop
COPY --from=build /out ./
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "FlowerShop.Api.dll"]
