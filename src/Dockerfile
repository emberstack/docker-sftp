FROM debian:stretch-slim

RUN apt-get update && \
    # - Install required packages
    #
    apt-get -y install members && \
    apt-get -y install acl && \
    #
    # - Install openssh-server
    apt-get -y install openssh-server && \
    #
    # - Install powershell
    apt-get install -y curl gnupg apt-transport-https && \
    curl https://packages.microsoft.com/keys/microsoft.asc | apt-key add - && \
    sh -c 'echo "deb [arch=amd64] https://packages.microsoft.com/repos/microsoft-debian-stretch-prod stretch main" > /etc/apt/sources.list.d/microsoft.list' && \
    apt-get update && \
    apt-get -y install powershell && \
    #
    # - Cleanup
    rm -rf /var/lib/apt/lists/* && \
    #
    # - Create OpenSSH directory
    mkdir -p /var/run/sshd && \
    #
    # - Remove default host keys
    rm -f /etc/ssh/ssh_host_*key*

EXPOSE 22

WORKDIR /sftp
COPY sftp .

ENTRYPOINT ["pwsh", "main.ps1", "Start"]