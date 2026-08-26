cd "$(dirname "$0")"
DOTNET_ENVIRONMENT=wikipedia-nollm dotnet run  --no-launch-profile --project ../../src/Minerva.MarkdownIndexer  -p:RunWorkingDirectory="$PWD"
