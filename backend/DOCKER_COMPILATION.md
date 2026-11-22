# Docker-based cBot compilation

The backend uses Docker.DotNet to create and remove a short-lived .NET 10 SDK container, and bind-mounts `CTrader:BuildRoot` into it. The temporary project and generated `.algo` must be visible at the same absolute path to both the backend and the Docker daemon. After compilation, source files remain in `CTrader:BotDirectory`; `.algo` files are copied into `backend/Algos/<bot-folder>/` by default. Set `CTrader:AlgoDirectory` to another absolute path and mount it when deploying the backend in a container.

When the backend runs directly on the Docker host, set `CTrader__BuildRoot` to a writable host directory. When the backend itself runs in Docker, mount the Docker socket and host build and bot directories at the configured paths. Docker.DotNet communicates through the socket, so the backend image does not need the Docker CLI. For example:

```yaml
services:
  backend:
    volumes:
      - /var/run/docker.sock:/var/run/docker.sock
      - /srv/ctrader-builds:/srv/ctrader-builds
      - /srv/ctrader-bots:/srv/ctrader-bots
    environment:
      CTrader__DockerImage: mcr.microsoft.com/dotnet/sdk:10.0-alpine
      CTrader__BuildRoot: /srv/ctrader-builds
      CTrader__BuildTimeoutMinutes: 5
      CTrader__BotDirectory: /srv/ctrader-bots
```

Create the host directories and grant the backend container's user write access. Avoid world-writable permissions for production. The compiler uses a persistent Docker volume named `ctrader-nuget-cache` for restored packages. Its build timeout defaults to five minutes and can be configured from one to sixty minutes.

## cTrader CLI backtests

The Backtesting page runs `ghcr.io/spotware/ctrader-console:latest` in a separate short-lived Docker container. Configure `CTrader:BacktestImage`, `CTrader:DockerEndpoint`, `CTrader:BuildRoot`, `CTrader:BacktestTimeoutMinutes`, and `CTrader:BacktestDirectory`. The image must be available to the selected Docker daemon. Backtest command options are passed as individual arguments, and the cTrader password is written only to a temporary password file mounted read-only into the container; the temporary workspace is removed after the run.

Each run is recorded in the `BacktestRuns` database table. Its HTML and JSON reports are stored under `<BacktestDirectory>/<bot-id>/<run-id>/` with filenames based on the cBot name. The database stores report paths and authenticated report URLs, plus the chosen backtest settings and local performance overview. cTrader credentials are not stored in the database or conversation history. The backtest report is analyzed locally and its metrics are not sent to Gemini.
