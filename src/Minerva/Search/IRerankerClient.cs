namespace Minerva.Search;

interface IRerankerClient
{
    Task<float[]> RankTexts(string query, List<string> texts, CancellationToken cancellationToken = default);
}