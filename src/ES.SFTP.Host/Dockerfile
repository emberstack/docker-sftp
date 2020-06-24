FROM mcr.microsoft.com/dotnet/core/aspnet:3.1 AS base
RUN apt-get update && \
    # - Install required packages
    #
    apt-get -y install members acl iputils-ping nano tini && \
    #
    # - Install openssh-server
    apt-get -y install openssh-server && \
    #
    # - Install sssd
    apt-get -y install sssd libpam-sss libnss-sss && \
    #
    # - Cleanup
    rm -rf /var/lib/apt/lists/* && \
    #
    # - Create OpenSSH directory
    mkdir -p /var/run/sshd && \
    #
    # - Remove default host keys
    rm -f /etc/ssh/ssh_host_*key*
WORKDIR /app
EXPOSE 22 25080

FROM mcr.microsoft.com/dotnet/core/sdk:3.1 AS build
WORKDIR /src
COPY ["ES.SFTP.Host/ES.SFTP.Host.csproj", "ES.SFTP.Host/"]
RUN dotnet restore "ES.SFTP.Host/ES.SFTP.Host.csproj"
COPY . .
WORKDIR "/src/ES.SFTP.Host"
RUN dotnet build "ES.SFTP.Host.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "ES.SFTP.Host.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["tini", "--", "dotnet", "ES.SFTP.Host.dll"]
