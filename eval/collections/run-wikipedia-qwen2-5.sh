cd "$(dirname "$0")"
DOTNET_ENVIRONMENT=wikipedia-qwen2-5 dotnet run  --no-launch-profile --project ../../src/Minerva.MarkdownIndexer  -p:RunWorkingDirectory="$PWD"
