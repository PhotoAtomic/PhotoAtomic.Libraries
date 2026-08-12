using PhotoAtomic.Clooney;

namespace PhotoAtomic.Clooney.Tests.TestGrains;

// Replica autonoma (senza Orleans/Darc) dei modelli usati da DeepClonerTests in Darc:
// stato event-sourced con la lista di eventi pendenti esclusa dal clone via [SkipClone]
// dichiarato sulla classe base, per esercitare anche le proprietà ereditate.

public abstract class Event
{
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}

public class MoneyDepositedEvent : Event
{
    public MoneyDepositedEvent(decimal amount) => Amount = amount;

    public decimal Amount { get; set; }
}

public class MoneyWithdrawnEvent : Event
{
    public MoneyWithdrawnEvent(decimal amount) => Amount = amount;

    public decimal Amount { get; set; }
}

public abstract class EventSourcedStateBase
{
    [SkipClone]
    public List<Event> PendingEventsList { get; set; } = new();
}

[Clonable]
public class BankAccountState : EventSourcedStateBase
{
    public decimal Balance { get; set; }

    public int TransactionCount { get; set; }

    public DateTime LastUpdate { get; set; }
}
