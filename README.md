# SFTP ([SSH File Transfer Protocol](https://en.wikipedia.org/wiki/SSH_File_Transfer_Protocol)) server using [OpenSSH](https://en.wikipedia.org/wiki/OpenSSH)
This project provides a Docker image for hosting a SFTP server. Included are `Docker` (`docker-cli` and `docker-compose`) and `Kubernetes` (`kubectl` and `helm`) deployment scripts

[![Pipeline](https://github.com/emberstack/docker-sftp/actions/workflows/pipeline.yaml/badge.svg)](https://github.com/emberstack/docker-sftp/actions/workflows/pipeline.yaml)
[![Release](https://img.shields.io/github/release/emberstack/docker-sftp.svg?style=flat-square)](https://github.com/emberstack/docker-sftp/releases/latest)
[![Docker Image](https://img.shields.io/docker/image-size/emberstack/sftp?style=flat-square)](https://hub.docker.com/r/emberstack/sftp)
[![Docker Pulls](https://img.shields.io/docker/pulls/emberstack/sftp?style=flat-square)](https://hub.docker.com/r/emberstack/sftp)
[![license](https://img.shields.io/github/license/emberstack/docker-sftp.svg?style=flat-square)](LICENSE)

> Supports architectures: `amd64`, `arm` and `arm64`

### Support
If you need help or found a bug, please feel free to open an issue on the [emberstack/docker-sftp](https://github.com/emberstack/docker-sftp) GitHub project.  

## Usage

The SFTP server can be easily deployed to any platform that can host containers based on Docker.
Below are deployment methods for:
- Docker CLI
- Docker-Compose
- Kubernetes using Helm (recommended for Kubernetes)

Process:
1) Create server configuration
2) Mount volumes as needed
3) Set host file for consistent server fingerprint

### Configuration

The SFTP server uses a `json` based configuration file for default server options and to define users. This file has to be mounted on `/app/config/sftp.json` inside the container.
Environment variable based configuration is not supported (see the `Advanced Configuration` section below for the reasons).

Below is the simplest configuration file for the SFTP server:

```json
{
    "Global": {
        "Chroot": {
            "Directory": "%h",
            "StartPath": "sftp"
        },
        "Directories": ["sftp"]
    },
    "Users": [
        {
            "Username": "demo",
            "Password": "demo"
        }
    ]
}
```
This configuration creates a user `demo` with the password `demo`. 
A directory "sftp" is created for each user in the own home and is accessible for read/write. 
The user is `chrooted` to the `/home/demo` directory. Upon connect, the start directory is `sftp`.

You can add additional users, default directories or customize start directories per user. You can also define the `UID` and `GID` for each user. See the `Advanced Configuration` section below for all configuration options.


### Deployment using Docker CLI

> Simple Docker CLI run

```shellsession
$ docker run -p 22:22 -d emberstack/sftp --name sftp
```
This will start a SFTP in the container `sftp` with the default configuration. You can connect to it and login with the `user: demo` and `password: demo`.

> Provide your configuration

```shellsession
$ docker run -p 22:22 -d emberstack/sftp --name sftp -v /host/sftp.json:/app/config/sftp.json:ro
```
This will override the default (`/app/config/sftp.json`) configuration with the one from the host `/host/sftp.json`.

> Mount a directory from the host for the user 'demo'

```shellsession
$ docker run -p 22:22 -d emberstack/sftp --name sftp -v /host/sftp.json:/app/config/sftp.json:ro -v /host/demo:/home/demo/sftp
```
This will mount the `demo` directory from the host on the `sftp` directory for the "demo" user.


### Deployment using Docker Compose

> Simple docker-compose configuration

Create a docker-compose configuration file:
```yaml
version: '3'
services:
  sftp:
    image: "emberstack/sftp"
    ports:
      - "22:22"
    volumes:
      - ../config-samples/sample.sftp.json:/app/config/sftp.json:ro
```
And run it using docker-compose
```shellsession
$ docker-compose -p sftp -f docker-compose.yaml up -d
```

The above configuration is available in the `deploy\docker-compose` folder in this repository. You can use it to start customizing the deployment for your environment.



### Deployment to Kubernetes using Helm

Use Helm to install the latest released chart:
```shellsession
$ helm repo add emberstack https://emberstack.github.io/helm-charts
$ helm repo update
$ helm upgrade --install sftp emberstack/sftp
```

You can customize the values of the helm deployment by using the following Values:

| Parameter                                                   | Description                                                                      | Default                                                 |
| ------------------------------------                        | -------------------------------------------------------------------------------- | ------------------------------------------------------- |
| `nameOverride`                                              | Overrides release name                                                           | `""`                                                    |
| `fullnameOverride`                                          | Overrides release fullname                                                       | `""`                                                    |
| `image.repository`                                          | Container image repository                                                       | `emberstack/sftp`                                       |
| `image.tag`                                                 | Container image tag                                                              | `latest`                                                |
| `image.pullPolicy`                                          | Container image pull policy                                                      | `Always` if `image.tag` is `latest`, else `IfNotPresent`|
| `storage.volumes`                                           | Defines additional volumes for the pod                                           | `{}`                                                    |
| `storage.volumeMounts`                                      | Defines additional volumes mounts for the sftp container                         | `{}`                                                    |
| `configuration`                                             | Allows the in-line override of the configuration values                          | `null`                                                  |
| `configuration.Global.Chroot.Directory`                     | Global chroot directory for the `sftp` user group. Can be overriden per-user     | `"%h"`                                                  |
| `configuration.Global.Chroot.StartPath`                     | Start path for the `sftp` user group. Can be overriden per-user                  | `"sftp"`                                                |
| `configuration.Global.Directories`                          | Directories that get created for all `sftp` users. Can be appended per user      | `["sftp"]`                                              |
| `configuration.Global.HostKeys.Ed25519`                     | Set the server's ED25519 private key                                             | `""`                                                    |
| `configuration.Global.HostKeys.Rsa`                         | Set the server's RSA private key                                                 | `""`                                                    |
| `configuration.Users`                                       | Array of users and their properties                                              | Contains `demo` user by default                         |
| `configuration.Users[].Username`                            | Set the user's username                                                          | N/A                                                     |
| `configuration.Users[].Password`                            | Set the user's password. If empty or `null`, password authentication is disabled | N/A                                                     |
| `configuration.Users[].PasswordIsEncrypted`                 | `true` or `false`. Indicates if the password value is already encrypted          | `false`                                                 |
| `configuration.Users[].AllowedHosts`                        | Set the user's allowed hosts. If empty, any host is allowed                      | `[]`                                                    |
| `configuration.Users[].PublicKeys`                          | Set the user's public keys                                                       | `[]`                                                    |
| `configuration.Users[].UID`                                 | Sets the user's UID.                                                             | `null`                                                  |
| `configuration.Users[].GID`                                 | Sets the user's GID. A group is created for this value and the user is included  | `null`                                                  |
| `configuration.Users[].Chroot`                              | If set, will override global `Chroot` settings for this user.                    | `null`                                                  |
| `configuration.Users[].Directories`                         | Array of additional directories created for this user                            | `null`                                                  |
| `configuration.Users[].Umask`                               | If set, will set a user-specific `umask` value for this user.                    | `null`                                                  |
| `initContainers`                                            | Additional initContainers for the pod                                            | `{}`                                                    |
| `resources`                                                 | Resource limits                                                                  | `{}`                                                    |
| `nodeSelector`                                              | Node labels for pod assignment                                                   | `{}`                                                    |
| `tolerations`                                               | Toleration labels for pod assignment                                             | `[]`                                                    |
| `affinity`                                                  | Node affinity for pod assignment                                                 | `{}`                                                    |

> Find us on [Helm Hub](https://hub.helm.sh/charts/emberstack)


## Advanced Configuration

TODO: This section is under development due to the number of configuration options being added. Please open an issue on the [emberstack/docker-sftp](https://github.com/emberstack/docker-sftp) project if you need help.

