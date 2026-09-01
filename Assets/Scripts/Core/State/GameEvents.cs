using Game.Core.Commands;

namespace Game.Core.Events
{
    public abstract record GameEvent;

    public record GameStartedEvent(int Seed) : GameEvent;
    public record GameEndedEvent(int WinnerId) : GameEvent;
    public record RotationStartedEvent(int RotationIndex) : GameEvent;
    public record SlotChangedEvent(int SlotIndex, string SlotType, int ActivePlayerId) : GameEvent;

    public record CardDrawnEvent(int PlayerId, int CardInstanceId) : GameEvent;
    public record CardPlayedEvent(int PlayerId, int CardInstanceId, int LaneIndex, int SlotIndex) : GameEvent;
    public record CardDiedEvent(int CardInstanceId, int LaneIndex, int SlotIndex, int KillerId) : GameEvent;
    public record CardMovedEvent(int CardInstanceId, int FromLane, int FromSlot, int ToLane, int ToSlot) : GameEvent;

    public record PlayerDamagedEvent(int PlayerId, int Amount, int DamageTaken) : GameEvent;
    public record PlayerHealedEvent(int PlayerId, int Amount, int DamageHealed) : GameEvent;
    public record CardDamagedEvent(int CardInstanceId, int Amount, int DamageTaken) : GameEvent;
    public record CardHealedEvent(int CardInstanceId, int Amount, int DamageHealed) : GameEvent;
    public record CardBuffedEvent(int CardInstanceId, int AttackChange, int HealthChange) : GameEvent;
    public record GoldGainedEvent(int PlayerId, int GoldGained) : GameEvent;
    public record GoldLostEvent(int PlayerId, int GoldLost) : GameEvent;

    public record CombatResolvedEvent(int LaneIndex) : GameEvent;
    public record EventTriggeredEvent(string EventId) : GameEvent;

    public record ShopRefreshedEvent(int PlayerId) : GameEvent;
    public record CardGrantedEvent(int PlayerId, int CardInstanceId, string DefinitionId, string Zone) : GameEvent;
    public record CardBoughtEvent(int PlayerId, int CardInstanceId, int Cost) : GameEvent;
    public record CardRemovedFromDeckEvent(int PlayerId, int CardInstanceId, int Cost) : GameEvent;
    public record DeckRerolledEvent(int PlayerId) : GameEvent;

    public record AugmentOfferedEvent(int PlayerId, string[] OptionIds) : GameEvent;
    public record AugmentSelectedEvent(int PlayerId, string AugmentId) : GameEvent;

    public record CommandRejectedEvent(Command Command, string Reason) : GameEvent;

    public record EnergyChangedEvent(int PlayerId, int NewEnergy, int NewCap) : GameEvent;

}