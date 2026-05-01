namespace Minerva.Readiness;

public sealed class ReadinessProbeMarker : IReadinessProbeMarker
{
    private volatile bool _probed;

    public bool Probed => _probed;

    public void MarkProbed() => _probed = true;
}
