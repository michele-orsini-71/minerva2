namespace Minerva.Readiness;

public interface IReadinessProbeMarker
{
    bool Probed { get; }
    void MarkProbed();
}
