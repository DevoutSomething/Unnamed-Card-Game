using System.Collections.Generic;

namespace Game.Core.State
{
    
public class Lane {
    public int Position;                            
    public string LaneTypeId;                       
    public Sublane P1;                             
    public Sublane P2;                              
    public List<string> LaneStatusEffects = new();  

    public Lane(int position, int slotsPerSide, string laneTypeId = null) {
        Position = position;
        LaneTypeId = laneTypeId;
        P1 = new Sublane(playerId: 0, maxSize: slotsPerSide);
        P2 = new Sublane(playerId: 1, maxSize: slotsPerSide);
    }

    public Sublane SublaneOf(int playerId) => playerId == 0 ? P1 : P2;

    public Sublane OpposingSide(int playerId) => playerId == 0 ? P2 : P1;
}
}
