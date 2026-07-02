namespace Game.Core.State
{
    public class GameState {
        public int Seed { get; }
        public int Tick { get; private set; } 
        public Player[] Players { get; }   
        public int ActivePlayerIndex { get; private set; }
        public Phase CurrentPhase { get; private set; }

        public bool IsGameOver {
            get {
                return Players[0].Health <= 0 || Players[1].Health <= 0;
            }
        }

        public Lane[] Lanes;
        public int SlotIndex { get; private set; }

        public Game.Core.Util.GameRng RNG { get; }
        
        public int NextCardInstanceId { get; set; } = 1;
        public GameState(int seed, int laneCount = 5, int slotsPerSide = 2) {
            Seed = seed;
            RNG = new Game.Core.Util.GameRng(seed);
            Players = new[] { new Player(0), new Player(1) };
            CurrentPhase = Phase.P1Action;
            Lanes = new Lane[laneCount];
            for (int i = 0; i < Lanes.Length; i++) {
                Lanes[i] = new Lane(position: i, slotsPerSide: slotsPerSide);
            }
        }
    }

    public enum Phase { P1Action, P1Spells, P2Action, P2Spells, Combat, Event, Augment }
}