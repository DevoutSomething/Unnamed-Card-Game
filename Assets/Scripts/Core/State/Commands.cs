namespace Game.Core.Commands
{
    
public abstract record Command(int PlayerId);

public record StartGameCommand(int Seed, string[] HeroIds = null) : Command(-1);

/// <summary>
/// Plays a card from hand. Guys use LaneIndex/SlotIndex to pick where they
/// stand. Spells ignore those and use the Target fields instead: exactly one of
/// TargetCardInstanceId / TargetPlayerId is set (or neither, for a spell that
/// needs no target). -1 means "not targeting that kind of thing".
/// </summary>
public record PlayCardCommand(
    int PlayerId,
    int CardInstanceId,
    int LaneIndex,
    int SlotIndex = -1,
    int TargetCardInstanceId = -1,
    int TargetPlayerId = -1
) : Command(PlayerId);

public record EndPhaseCommand(int PlayerId) : Command(PlayerId);

public record BuyCardCommand(int PlayerId, int ShopCardInstanceId) : Command(PlayerId);
public record RemoveCardFromDeckCommand(int PlayerId, int DeckCardInstanceId) : Command(PlayerId);
public record EndShopCommand(int PlayerId) : Command(PlayerId);

/// <summary>Picks one of the three augments offered this phase. AugmentId must
/// be one of the player's own current offers.</summary>
public record SelectAugmentCommand(int PlayerId, string AugmentId) : Command(PlayerId);

/// <summary>Polled by every client's GameController.Update() while the augment
/// timer runs; a no-op unless the resolver's own clock confirms the window has
/// closed, so it's safe to submit speculatively and repeatedly. On expiry it
/// picks FOR anyone who hasn't chosen — nobody stalls the rotation by walking
/// away from the screen. No PlayerId: it resolves the phase for both players.</summary>
public record ForceEndAugmentCommand() : Command(-1);

/// <summary>Polled by every client's GameController.Update() while the shop's
/// timer is running (see CommandResolver.ShopTimeLimitSeconds); a no-op unless
/// the resolver's own clock confirms the deadline has actually passed, so it's
/// safe to submit speculatively and repeatedly. No PlayerId — it advances the
/// rotation for both players at once, like Combat/Event settling on their own.</summary>
public record ForceEndShopCommand() : Command(-1);
}
