# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution + project files first for better layer caching
COPY SaaSSkeleton.sln ./
COPY apps/api/Api/Api.csproj apps/api/Api/

RUN dotnet restore apps/api/Api/Api.csproj

# Copy everything else
COPY . ./
RUN dotnet publish apps/api/Api/Api.csproj -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish ./
EXPOSE 8080
ENTRYPOINT ["dotnet", "Api.dll"]