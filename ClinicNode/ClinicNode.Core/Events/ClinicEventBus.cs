// JJQR - AI (Nov 21, 2026) - in-process stand-in for SignalR (PatientCheckedIn / QueueUpdated / PaymentUpdated).
namespace ClinicNode.Core.Events;

public enum ClinicEventKind
{
    QueueUpdated,
    PatientCheckedIn,
    PaymentUpdated,
    PatientsChanged
}

public sealed record ClinicEvent(ClinicEventKind Kind, Guid? BookingId = null, Guid? DoctorId = null, Guid? PatientId = null);

public interface IClinicEventBus
{
    event Action<ClinicEvent>? Changed;
    void Publish(ClinicEvent ev);
}

public sealed class ClinicEventBus : IClinicEventBus
{
    public event Action<ClinicEvent>? Changed;

    public void Publish(ClinicEvent ev) => Changed?.Invoke(ev);
}
// JJQR - END
