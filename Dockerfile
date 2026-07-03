FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore
RUN dotnet publish cars-website-api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
# QuestPDF (invoice PDFs) renders text via SkiaSharp, which needs a real font on disk -
# the base aspnet image ships none, so without this every invoice PDF either fails to
# render text or (via jsPDF's Type1 client-side fallback, which lacks Polish glyphs
# entirely) mangles diacritics. Liberation Sans is metric-compatible with Arial and
# covers Polish (Latin Extended-A).
RUN apt-get update && apt-get install -y --no-install-recommends fontconfig fonts-liberation \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
EXPOSE 5000
ENTRYPOINT ["sh", "-c", "dotnet cars-website-api.dll --urls http://+:${PORT:-5000}"]
