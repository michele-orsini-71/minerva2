namespace Minerva.Ingestion;

public interface ILlmAvailabilityProbe
{
    Task CheckAvailabilityAsync(CancellationToken ct = default);
}
