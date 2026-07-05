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
}
