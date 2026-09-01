namespace Game.Core.Commands
{
    
public abstract record Command(int PlayerId);

public record StartGameCommand(int Seed) : Command(-1);

public record PlayCardCommand(
    int PlayerId,
    int CardInstanceId,
    int LaneIndex,
    int SlotIndex = -1     
) : Command(PlayerId);

public record EndPhaseCommand(int PlayerId) : Command(PlayerId);

public record BuyCardCommand(int PlayerId, int ShopCardInstanceId) : Command(PlayerId);
public record RemoveCardFromDeckCommand(int PlayerId, int DeckCardInstanceId) : Command(PlayerId);
public record RerollDeckCommand(int PlayerId) : Command(PlayerId);
public record EndShopCommand(int PlayerId) : Command(PlayerId);

/// <summary>Polled by every client's GameController.Update() while the shop's
/// timer is running (see CommandResolver.ShopTimeLimitSeconds); a no-op unless
/// the resolver's own clock confirms the deadline has actually passed, so it's
/// safe to submit speculatively and repeatedly. No PlayerId — it advances the
/// rotation for both players at once, like Combat/Event settling on their own.</summary>
public record ForceEndShopCommand() : Command(-1);
}
