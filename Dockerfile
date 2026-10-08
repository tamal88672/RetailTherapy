FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/RetailTherapy.Web/RetailTherapy.Web.csproj src/RetailTherapy.Web/
RUN dotnet restore src/RetailTherapy.Web/RetailTherapy.Web.csproj -r linux-x64
COPY src/ src/
# ReadyToRun = precompiled code, which shortens Cloud Run cold starts
RUN dotnet publish src/RetailTherapy.Web/RetailTherapy.Web.csproj \
    -c Release -r linux-x64 --self-contained false -p:PublishReadyToRun=true --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
USER $APP_UID
ENTRYPOINT ["dotnet", "RetailTherapy.Web.dll"]
