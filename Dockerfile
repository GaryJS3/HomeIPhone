FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY publish/ .
ENV ASPNETCORE_URLS=http://0.0.0.0:8080 PhoneServer__DataPath=/data
# Root keeps named-volume initialization and UDP/69 binding reliable on Linux.
USER root
EXPOSE 8080/tcp 69/udp
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 CMD ["dotnet", "HomeIPhone.dll", "--healthcheck"]
ENTRYPOINT ["dotnet", "HomeIPhone.dll"]
