# SFTP ([SSH File Transfer Protocol](https://en.wikipedia.org/wiki/SSH_File_Transfer_Protocol)) server using [OpenSSH](https://en.wikipedia.org/wiki/OpenSSH)
This project provides a Docker image for hosting a SFTP server.

[![Build Status](https://dev.azure.com/emberstack/OpenSource/_apis/build/status/docker-sftp?branchName=master)](https://dev.azure.com/emberstack/OpenSource/_build/latest?definitionId=16&branchName=master)
[![Release](https://img.shields.io/github/release/emberstack/docker-sftp.svg?style=flat-square)](https://github.com/emberstack/docker-sftp/releases/latest)
[![GitHub Tag](https://img.shields.io/github/tag/emberstack/docker-sftp.svg?style=flat-square)](https://github.com/emberstack/docker-sftp/releases/latest)
[![Docker Image](https://images.microbadger.com/badges/image/emberstack/sftp.svg)](https://microbadger.com/images/emberstack/sftp)
[![Docker Version](https://images.microbadger.com/badges/version/emberstack/sftp.svg)](https://microbadger.com/images/emberstack/sftp)
[![Docker Pulls](https://img.shields.io/docker/pulls/emberstack/sftp.svg?style=flat-square)](https://hub.docker.com/r/emberstack/sftp)
[![Docker Stars](https://img.shields.io/docker/stars/emberstack/sftp.svg?style=flat-square)](https://hub.docker.com/r/remberstack/sftp)
[![license](https://img.shields.io/github/license/emberstack/docker-sftp.svg?style=flat-square)](LICENSE)


> Supports architectures: `amd64`. Coming soon: `arm` and `arm64`

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
| `configuration.Users`                                       | Array of users and their properties                                              | Contains `demo` user by default                         |
| `configuration.Users[].Username`                            | Set the user's username                                                          | N/A                                                     |
| `configuration.Users[].Password`                            | Set the user's password. If empty or `null`, password authentication is disabled | N/A                                                     |
| `configuration.Users[].PasswordIsEncrypted`                 | `true` or `false`. Indicates if the password value is already encrypted          | `false`                                                 |
| `configuration.Users[].UID`                                 | Sets the user's UID.                                                             | `null`                                                  |
| `configuration.Users[].GID`                                 | Sets the user's GID. A group is created for this value and the user is included  | `null`                                                  |
| `configuration.Users[].Chroot`                              | If set, will override global `Chroot` settings for this user.                    | `null`                                                  |
| `configuration.Users[].Directories`                         | Array of additional directories created for this user                            | `null`                                                  |
| `initContainers`                                            | Additional initContainers for the pod                                            | `{}`                                                    |
| `resources`                                                 | Resource limits                                                                  | `{}`                                                    |
| `nodeSelector`                                              | Node labels for pod assignment                                                   | `{}`                                                    |
| `tolerations`                                               | Toleration labels for pod assignment                                             | `[]`                                                    |
| `affinity`                                                  | Node affinity for pod assignment                                                 | `{}`                                                    |

> Find us on [Helm Hub](https://hub.helm.sh/charts/emberstack)
