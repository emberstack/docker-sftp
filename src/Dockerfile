#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:6.0-jammy AS base
RUN apt-get update && \
    # - Install required packages
    #
    apt-get -y install members acl iputils-ping nano tini curl && \
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

FROM mcr.microsoft.com/dotnet/sdk:6.0-jammy AS build
WORKDIR /src
COPY ["ES.SFTP/ES.SFTP.csproj", "ES.SFTP/"]
RUN dotnet restore "ES.SFTP/ES.SFTP.csproj"
COPY . .
WORKDIR "/src/ES.SFTP"
RUN dotnet build "ES.SFTP.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "ES.SFTP.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["tini", "--", "dotnet", "ES.SFTP.dll"]