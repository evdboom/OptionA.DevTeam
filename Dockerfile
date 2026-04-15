# DevTeam CLI container image
# Build:  docker build -t devteam .
# Run:    docker run -it --rm -e GITHUB_TOKEN=<token> devteam \
#           devteam init --goal "your goal here" && devteam run-loop --max-iterations 10

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src
COPY . .
RUN dotnet pack src/DevTeam.Cli/DevTeam.Cli.csproj -c Release -o /packages

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS runtime

# Install DevTeam CLI from local package
COPY --from=build /packages/*.nupkg /packages/
RUN dotnet tool install --global devteam \
        --add-source /packages \
        --version '*-*' \
    || dotnet tool install --global devteam --add-source /packages

ENV PATH="$PATH:/root/.dotnet/tools"

# Install common tools used by DevTeam agents
RUN apt-get update && apt-get install -y --no-install-recommends \
        git \
        curl \
        ripgrep \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /workspace

# Entrypoint is a shell so the caller can chain commands:
#   docker run devteam sh -c "devteam init --goal '...' && devteam run-loop ..."
ENTRYPOINT ["/bin/sh", "-c"]
CMD ["devteam --help"]
