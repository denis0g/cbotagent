# Docker-based cBot compilation

The backend uses Docker.DotNet to create and remove a short-lived .NET 10 SDK container, and bind-mounts `CTrader:BuildRoot` into it. The temporary project and generated `.algo` must be visible at the same absolute path to both the backend and the Docker daemon.

When the backend runs directly on the Docker host, set `CTrader__BuildRoot` to a writable host directory. When the backend itself runs in Docker, mount the Docker socket and host build and bot directories at the configured paths. Docker.DotNet communicates through the socket, so the backend image does not need the Docker CLI. For example:

```yaml
services:
  backend:
    volumes:
      - /var/run/docker.sock:/var/run/docker.sock
      - /srv/ctrader-builds:/srv/ctrader-builds
      - /srv/ctrader-bots:/srv/ctrader-bots
    environment:
      CTrader__DockerImage: mcr.microsoft.com/dotnet/sdk:10.0
      CTrader__BuildRoot: /srv/ctrader-builds
      CTrader__BuildTimeoutMinutes: 5
      CTrader__BotDirectory: /srv/ctrader-bots
```

Create the host directories and grant the backend container's user write access. Avoid world-writable permissions for production. The compiler uses a persistent Docker volume named `ctrader-nuget-cache` for restored packages. Its build timeout defaults to five minutes and can be configured from one to sixty minutes.
